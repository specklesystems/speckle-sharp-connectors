using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.DUI.Bindings;

// POC: Send Commands share all commands from BasicBindings + some, this pattern should be revised
public class SendBindingUICommands(IBrowserBridge bridge) : BasicConnectorBindingCommands(bridge)
{
  private const string REFRESH_SEND_FILTERS_UI_COMMAND_NAME = "refreshSendFilters";
  private const string SET_MODELS_EXPIRED_UI_COMMAND_NAME = "setModelsExpired";
  private const string SET_MODEL_SEND_RESULT_UI_COMMAND_NAME = "setModelSendResult";
  private const string SET_FILTER_OBJECT_IDS_COMMAND_NAME = "setFilterObjectIds";

  // POC.. the only reasons this needs the bridge is to send? realtionship to these messages and the bridge is unclear
  public async Task RefreshSendFilters() =>
    await Bridge.Send(REFRESH_SEND_FILTERS_UI_COMMAND_NAME).ConfigureAwait(false);

  public async Task SetModelsExpired(IEnumerable<string> expiredModelIds) =>
    await Bridge.Send(SET_MODELS_EXPIRED_UI_COMMAND_NAME, expiredModelIds).ConfigureAwait(false);

  public async Task SetFilterObjectIds(string modelCardId, List<string> objectIds) =>
    await Bridge.Send(SET_FILTER_OBJECT_IDS_COMMAND_NAME, new { modelCardId, objectIds }).ConfigureAwait(false);

  public async Task SetModelSendResult(
    string modelCardId,
    string versionId,
    IEnumerable<SendConversionResult> sendConversionResults
  ) =>
    await Bridge
      .Send(
        SET_MODEL_SEND_RESULT_UI_COMMAND_NAME,
        new
        {
          modelCardId,
          versionId,
          sendConversionResults
        }
      )
      .ConfigureAwait(false);
}
