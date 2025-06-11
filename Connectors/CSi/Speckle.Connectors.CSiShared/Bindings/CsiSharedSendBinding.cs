using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.CSiShared.HostApp;
using Speckle.Connectors.CSiShared.Utils;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Settings;
using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.CSiShared.Bindings;

public sealed class CsiSharedSendBinding : ISendBinding
{
  public string Name => "sendBinding";
  public SendBindingUICommands Commands { get; }
  public IBrowserBridge Parent { get; }

  private readonly List<ISendFilter> _sendFilters;
  private readonly ICancellationManager _cancellationManager;
  private readonly ICsiApplicationService _csiApplicationService;
  private readonly ICsiConversionSettingsFactory _csiConversionSettingsFactory;
  private readonly ISendOperationManagerFactory _sendOperationManagerFactory;

  public CsiSharedSendBinding(
    IBrowserBridge parent,
    IEnumerable<ISendFilter> sendFilters,
    ICancellationManager cancellationManager,
    ICsiConversionSettingsFactory csiConversionSettingsFactory,
    ICsiApplicationService csiApplicationService,
    ISendOperationManagerFactory sendOperationManagerFactory
  )
  {
    _sendFilters = sendFilters.ToList();
    _cancellationManager = cancellationManager;
    Parent = parent;
    Commands = new SendBindingUICommands(parent);
    _csiConversionSettingsFactory = csiConversionSettingsFactory;
    _csiApplicationService = csiApplicationService;
    _sendOperationManagerFactory = sendOperationManagerFactory;
  }

  public List<ISendFilter> GetSendFilters() => _sendFilters;

  public List<ICardSetting> GetSendSettings() => [];

  public async Task Send(string modelCardId)
  {
    using var manager = _sendOperationManagerFactory.Create();
    await manager.Process(
      Commands,
      modelCardId,
      (sp, _) =>
        sp.GetRequiredService<IConverterSettingsStore<CsiConversionSettings>>()
          .Initialize(_csiConversionSettingsFactory.Create(_csiApplicationService.SapModel)),
      card => card.SendFilter.NotNull().RefreshObjectIds().Select(DecodeObjectIdentifier).ToList()
    );
  }

  private ICsiWrapper DecodeObjectIdentifier(string encodedId)
  {
    var (type, name) = ObjectIdentifier.Decode(encodedId);
    return CsiWrapperFactory.Create(type, name);
  }

  public void CancelSend(string modelCardId)
  {
    _cancellationManager.CancelOperation(modelCardId);
  }
}
