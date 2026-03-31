using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Settings;
using Speckle.Connectors.Revit.Operations.Receive;
using Speckle.Connectors.Revit.Plugin;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Connectors.Revit.Bindings;

public sealed class RevitReceiveBinding : IReceiveBinding
{
  private readonly ICancellationManager _cancellationManager;
  private readonly ILogger<RevitReceiveBinding> _logger;
  private readonly Operations.Receive.Settings.ToHostSettingsManager _toHostSettingsManager;
  private readonly IRevitConversionSettingsFactory _revitConversionSettingsFactory;
  private readonly IReceiveOperationManagerFactory _receiveOperationManagerFactory;
  private readonly ReceiveBindingUICommands _commands;

  public string Name => "receiveBinding";
  public IBrowserBridge Parent { get; }

  public RevitReceiveBinding(
    ICancellationManager cancellationManager,
    IBrowserBridge parent,
    ILogger<RevitReceiveBinding> logger,
    Operations.Receive.Settings.ToHostSettingsManager toHostSettingsManager,
    IRevitConversionSettingsFactory revitConversionSettingsFactory,
    IReceiveOperationManagerFactory receiveOperationManagerFactory,
    DocumentModelStore store,
    ITopLevelExceptionHandler topLevelExceptionHandler
  )
  {
    _cancellationManager = cancellationManager;
    Parent = parent;
    _logger = logger;
    _toHostSettingsManager = toHostSettingsManager;
    _revitConversionSettingsFactory = revitConversionSettingsFactory;
    _receiveOperationManagerFactory = receiveOperationManagerFactory;

    _commands = new ReceiveBindingUICommands(parent);
    store.ReceiverSettingsChanged += (_, e) =>
      topLevelExceptionHandler.FireAndForget(async () =>
        await _commands.SetModelsExpired(new[] { e.ModelCardId }));
  }

#pragma warning disable CA1024
  public List<ICardSetting> GetReceiveSettings() =>
    [new Operations.Receive.Settings.ReceiveReferencePointSetting(), new ReceiveInstancesAsFamiliesSetting()];
#pragma warning restore CA1024

  public void CancelReceive(string modelCardId) => _cancellationManager.CancelOperation(modelCardId);

  public async Task Receive(string modelCardId)
  {
    using var manager = _receiveOperationManagerFactory.Create();
    await manager.Process(
      _commands,
      modelCardId,
      (sp, card) =>
      {
        sp.GetRequiredService<IConverterSettingsStore<RevitConversionSettings>>()
          .Initialize(
            _revitConversionSettingsFactory.Create(
              DetailLevelType.Coarse, // TODO figure out
              _toHostSettingsManager.GetReferencePointSetting(card),
              false,
              true,
              false,
              false,
              _toHostSettingsManager.GetReceiveInstancesAsFamiliesSetting(card)
            )
          );
      },
      async (_, processor) =>
      {
        try
        {
          return await processor();
        }
        catch (SpeckleRevitTaskException ex)
        {
          await SpeckleRevitTaskException.ProcessException(modelCardId, ex, _logger, _commands);
          return null;
        }
      }
    );
  }
}
