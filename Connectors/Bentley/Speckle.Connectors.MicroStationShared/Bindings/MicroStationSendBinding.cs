using Bentley.MstnPlatformNET;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Exceptions;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Settings;
using Speckle.Connectors.MicroStation.Operations.Send;
using Speckle.Connectors.MicroStation.Operations.Send.Filters;
using Speckle.Converters.Common;
using Speckle.Converters.MicroStation.Settings;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.MicroStation.Bindings;

public class MicroStationSendBinding(
  IBrowserBridge parent,
  ICancellationManager cancellationManager,
  IMicroStationConversionSettingsFactory conversionSettingsFactory,
  IThreadContext threadContext,
  ISendOperationManagerFactory sendOperationManagerFactory,
  MicroStationElementGatherer elementGatherer
) : ISendBinding
{
  public string Name => "sendBinding";
  public IBrowserBridge Parent { get; } = parent;
  public SendBindingUICommands Commands { get; } = new(parent);

  public List<ISendFilter> GetSendFilters() =>
    [
      new MicroStationEverythingFilter { IsDefault = true },
      new MicroStationSelectionFilter(),
      new MicroStationLevelFilter(),
    ];

  public List<ICardSetting> GetSendSettings() => [new SendIncludeReferencesSetting()];

  public async Task Send(string modelCardId) =>
    await threadContext.RunOnMainAsync(async () => await SendInternal(modelCardId));

  private async Task SendInternal(string modelCardId)
  {
    using var manager = sendOperationManagerFactory.Create();
    var (fileName, fileSizeBytes) = GetFileInfo();
    await manager.Process(Commands, modelCardId, InitializeConverterSettings, GatherElements, fileName, fileSizeBytes);
  }

  private static (string? fileName, long? fileSizeBytes) GetFileInfo()
  {
    try
    {
      string? path = Session.Instance?.GetActiveDgnFile()?.GetFileName();
      if (path != null && System.IO.File.Exists(path))
      {
        var info = new System.IO.FileInfo(path);
        return (info.Name, info.Length);
      }
    }
    catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
    {
      // best-effort telemetry only
    }
    return (null, null);
  }

  private void InitializeConverterSettings(IServiceProvider serviceProvider, SenderModelCard modelCard)
  {
    DPN.DgnModel activeModel =
      Session.Instance?.GetActiveDgnModel() ?? throw new InvalidOperationException("No active MicroStation model.");
    serviceProvider
      .GetRequiredService<IConverterSettingsStore<MicroStationConversionSettings>>()
      .Initialize(
        conversionSettingsFactory.Create(activeModel, MicroStationSendSettings.GetIncludeReferences(modelCard))
      );
  }

  private Task<IReadOnlyList<MicroStationRootObject>> GatherElements(
    SenderModelCard modelCard,
    IProgress<Speckle.Sdk.Pipelines.Progress.CardProgress> onOperationProgressed
  )
  {
    List<string> selectedIds = modelCard.SendFilter.NotNull().RefreshObjectIds();
    bool includeReferences = MicroStationSendSettings.GetIncludeReferences(modelCard);
    if (selectedIds.Count == 0 && !includeReferences)
    {
      throw new SpeckleSendFilterException("No objects found to convert. Please update your publish filter.");
    }

    onOperationProgressed.Report(new Speckle.Sdk.Pipelines.Progress.CardProgress("Getting elements...", null));

    DPN.DgnModel activeModel =
      Session.Instance?.GetActiveDgnModel() ?? throw new InvalidOperationException("No active MicroStation model.");

    // References ride along only on whole-model sends — a selection/level publish means exactly
    // what the user picked in the active model.
    bool walkReferences = includeReferences && modelCard.SendFilter is MicroStationEverythingFilter;
    IReadOnlyList<MicroStationRootObject> elements = elementGatherer.Gather(activeModel, selectedIds, walkReferences);

    if (elements.Count == 0)
    {
      throw new SpeckleSendFilterException("No objects found to convert. Please update your publish filter.");
    }
    return Task.FromResult(elements);
  }

  public void CancelSend(string modelCardId) => cancellationManager.CancelOperation(modelCardId);
}
