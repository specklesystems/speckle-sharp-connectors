using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.DUI.Bindings;

public interface ISendBindingUICommands
{
  Task RefreshSendFilters();

  Task SetModelsExpired(IEnumerable<string> expiredModelIds);
  Task SetModelError(string modelCardId, Exception exception);

  Task SetModelSendResult(
    string modelCardId,
    string versionId,
    IEnumerable<SendConversionResult> sendConversionResults
  );

  IBrowserBridge Bridge { get; }
}
