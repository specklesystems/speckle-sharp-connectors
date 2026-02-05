using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.DUI.Bindings;

public interface IReceiveBindingUICommands
{
  Task SetModelError(string modelCardId, Exception exception);

  Task SetModelReceiveResult(
    string modelCardId,
    IEnumerable<string> bakedObjectIds,
    IEnumerable<ConversionResult> conversionResults
  );

  IBrowserBridge Bridge { get; }
}

public sealed class ReceiveBindingUICommands : BasicConnectorBindingCommands, IReceiveBindingUICommands
{
  // POC: put here events once we needed for receive specific
  private const string SET_MODEL_RECEIVE_RESULT_UI_COMMAND_NAME = "setModelReceiveResult";

  public ReceiveBindingUICommands(IBrowserBridge bridge)
    : base(bridge) { }

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
