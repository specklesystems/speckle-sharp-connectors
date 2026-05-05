using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Exceptions;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Settings;
using Speckle.Connectors.MicroStation.Operations.Send.Filters;
using Speckle.Connectors.MicroStation.Plugin;
using Speckle.Converter.MicroStation.Settings;
using Speckle.Converters.Common;
using Speckle.Sdk.Common;
using Speckle.Sdk.Pipelines.Progress;

namespace Speckle.Connectors.MicroStation.Bindings;

public class MicroStationSendBinding : ISendBinding
{
  public string Name => "sendBinding";
  public IBrowserBridge Parent { get; }
  public SendBindingUICommands Commands { get; }

  private readonly ICancellationManager _cancellationManager;
  private readonly IMicroStationConversionSettingsFactory _conversionSettingsFactory;
  private readonly IThreadContext _threadContext;
  private readonly ISendOperationManagerFactory _sendOperationManagerFactory;

  public MicroStationSendBinding(
    IBrowserBridge parent,
    ICancellationManager cancellationManager,
    IMicroStationConversionSettingsFactory conversionSettingsFactory,
    IThreadContext threadContext,
    ISendOperationManagerFactory sendOperationManagerFactory
  )
  {
    Parent = parent;
    Commands = new SendBindingUICommands(parent);
    _cancellationManager = cancellationManager;
    _conversionSettingsFactory = conversionSettingsFactory;
    _threadContext = threadContext;
    _sendOperationManagerFactory = sendOperationManagerFactory;
  }

  public List<ISendFilter> GetSendFilters() =>
    [
      new MicroStationEverythingFilter { IsDefault = true },
      new MicroStationSelectionFilter(),
      new MicroStationLevelFilter(),
    ];

  public List<ICardSetting> GetSendSettings() => [];

  public async Task Send(string modelCardId) =>
    await _threadContext.RunOnMainAsync(async () => await SendInternal(modelCardId));

  private async Task SendInternal(string modelCardId)
  {
    using var manager = _sendOperationManagerFactory.Create();
    var (fileName, fileSizeBytes) = GetFileInfo();
    await manager.Process(Commands, modelCardId, InitializeConverterSettings, GetMicroStationElements, fileName, fileSizeBytes);
  }

  private (string? fileName, long? fileSizeBytes) GetFileInfo()
  {
    var app = MsApp.TryGetInstance();
    if (app?.HasActiveDesignFile != true)
    {
      return (null, null);
    }

    var path = app.ActiveDesignFile.FullName;
    if (!File.Exists(path))
    {
      return (null, null);
    }

    var info = new FileInfo(path);
    return (info.Name, info.Length);
  }

  private void InitializeConverterSettings(IServiceProvider serviceProvider, SenderModelCard modelCard) =>
    serviceProvider
      .GetRequiredService<IConverterSettingsStore<MicroStationConversionSettings>>()
      .Initialize(_conversionSettingsFactory.Create());

  private async Task<IReadOnlyList<Element>> GetMicroStationElements(
    SenderModelCard modelCard,
    IProgress<CardProgress> onOperationProgressed
  )
  {
    var selectedIds = modelCard.SendFilter.NotNull().RefreshObjectIds();
    if (selectedIds.Count == 0)
    {
      throw new SpeckleSendFilterException("No objects found to convert. Please update your publish filter.");
    }

    onOperationProgressed.Report(new CardProgress("Getting elements...", null));

    var model = MsApp.ActiveModel ?? throw new InvalidOperationException("No active MicroStation model.");
    var idSet = new HashSet<string>(selectedIds);
    var elements = new List<Element>(idSet.Count);

    var enumerator = model.GraphicalElementCache.Scan(new MSIDGN.ElementScanCriteriaClass());
    while (enumerator.MoveNext())
    {
      var element = enumerator.Current;
      if (element != null && idSet.Contains(element.ID.ToString()))
      {
        elements.Add(element);
      }
    }

    await Task.CompletedTask;
    return elements;
  }

  public void CancelSend(string modelCardId) => _cancellationManager.CancelOperation(modelCardId);
}
