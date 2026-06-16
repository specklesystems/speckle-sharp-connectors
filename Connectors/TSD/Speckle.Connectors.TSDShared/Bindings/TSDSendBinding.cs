using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Settings;
using Speckle.Connectors.TSDShared.HostApp;
using Speckle.Connectors.TSDShared.Operations.Send.Results;
using Speckle.Connectors.TSDShared.Settings;
using Speckle.Newtonsoft.Json.Linq;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.TSDShared.Bindings;

internal sealed class TSDSendBinding : ISendBinding
{
  public string Name => "sendBinding";
  public IBrowserBridge Parent { get; }
  public SendBindingUICommands Commands { get; }

  private readonly List<ISendFilter> _sendFilters;
  private readonly ICancellationManager _cancellationManager;
  private readonly ISendOperationManagerFactory _sendOperationManagerFactory;
  private readonly ITSDApplicationService _applicationService;

  public TSDSendBinding(
    IBrowserBridge parent,
    IEnumerable<ISendFilter> sendFilters,
    ICancellationManager cancellationManager,
    ISendOperationManagerFactory sendOperationManagerFactory,
    ITSDApplicationService applicationService
  )
  {
    Parent = parent;
    Commands = new SendBindingUICommands(parent);
    _sendFilters = sendFilters.ToList();
    _cancellationManager = cancellationManager;
    _sendOperationManagerFactory = sendOperationManagerFactory;
    _applicationService = applicationService;
  }

  public List<ISendFilter> GetSendFilters() => _sendFilters;

  public List<ICardSetting> GetSendSettings()
  {
    var settings = new List<ICardSetting>();

    if (_applicationService.LoadingNames.Count > 0)
    {
      settings.Add(new TsdLoadingSetting([], _applicationService.LoadingNames));
    }

    settings.Add(new TsdResultTypeSetting([]));

    return settings;
  }

  public async Task Send(string modelCardId)
  {
    using var manager = _sendOperationManagerFactory.Create();
    await manager.Process(
      Commands,
      modelCardId,
      (serviceProvider, card) =>
      {
        var settings = serviceProvider.GetRequiredService<TsdConversionSettings>();
        settings.SelectedLoadings = ReadArraySetting(card, "loadCasesAndCombinations");
        settings.SelectedResultTypes = ReadArraySetting(card, "resultTypes");
      },
      async card =>
        await _applicationService
          .GetObjectsForSendAsync(card.SendFilter.NotNull().RefreshObjectIds())
          .ConfigureAwait(false),
      null,
      null
    );
  }

  private static IReadOnlyList<string> ReadArraySetting(SenderModelCard card, string settingId)
  {
    var setting = card.Settings?.FirstOrDefault(s => s.Id == settingId);
    return (setting?.Value as JArray)?.Select(value => value.ToString()).ToList() ?? [];
  }

  public void CancelSend(string modelCardId) => _cancellationManager.CancelOperation(modelCardId);
}
