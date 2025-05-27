using Speckle.Connectors.Common.Conversion;

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
}
