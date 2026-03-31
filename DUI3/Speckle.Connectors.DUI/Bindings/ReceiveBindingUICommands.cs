using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;

namespace Speckle.Connectors.DUI.Bindings;

public interface IReceiveBindingUICommands
{
  Task SetModelError(string modelCardId, Exception exception);

  Task SetModelsExpired(IEnumerable<string> expiredModelIds);

  Task SetModelReceiveResult(
    string modelCardId,
    IEnumerable<string> bakedObjectIds,
    IEnumerable<ConversionResult> conversionResults
  );

  IBrowserBridge Bridge { get; }
}

public sealed class ReceiveBindingUICommands : BasicConnectorBindingCommands, IReceiveBindingUICommands
{
  private const string SET_MODELS_EXPIRED_UI_COMMAND_NAME = "setModelsExpired";
  private const string SET_MODEL_RECEIVE_RESULT_UI_COMMAND_NAME = "setModelReceiveResult";

  public ReceiveBindingUICommands(IBrowserBridge bridge)
    : base(bridge) { }

  public static ReceiveBindingUICommands CreateAndSubscribe(
    IBrowserBridge bridge,
    DocumentModelStore store,
    ITopLevelExceptionHandler topLevelExceptionHandler
  )
  {
    var commands = new ReceiveBindingUICommands(bridge);
    store.ReceiverSettingsChanged += (_, e) =>
      topLevelExceptionHandler.FireAndForget(async () =>
        await commands.SetModelsExpired(new[] { e.ModelCardId }));
    return commands;
  }

  public async Task SetModelsExpired(IEnumerable<string> expiredModelIds) =>
    await Bridge.Send(SET_MODELS_EXPIRED_UI_COMMAND_NAME, expiredModelIds);

  public async Task SetModelReceiveResult(
    string modelCardId,
    IEnumerable<string> bakedObjectIds,
    IEnumerable<ConversionResult> conversionResults
  )
  {
    await Bridge.Send(
      SET_MODEL_RECEIVE_RESULT_UI_COMMAND_NAME,
      new
      {
        ModelCardId = modelCardId,
        bakedObjectIds,
        conversionResults
      }
    );
  }
}
