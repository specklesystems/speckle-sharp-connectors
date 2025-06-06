using Microsoft.Extensions.Logging;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Logging;
using Speckle.Sdk;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.Revit.Plugin;

#pragma warning disable CA1032
public class SpeckleRevitTaskException(Exception exception) : SpeckleException("Revit operation failed", exception)
#pragma warning restore CA1032
{
  public static async Task ProcessException<T>(
    string modelCardId,
    SpeckleRevitTaskException ex,
    ILogger<T> logger,
    IReceiveBindingUICommands commands
  )
    where T : IBinding
  {
    Exception e = ex.InnerException.NotNull();
    while (e is SpeckleRevitTaskException srte)
    {
      e = srte.InnerException.NotNull();
    }
    if (e is OperationCanceledException)
    {
      // SWALLOW -> UI handles it immediately, so we do not need to handle anything for now!
      // Idea for later -> when cancel called, create promise from UI to solve it later with this catch block.
      // So have 3 state on UI -> Cancellation clicked -> Cancelling -> Cancelled
      return;
    }
    //log everything though
    logger.LogModelCardHandledError(ex);
    //always process the inner exception
    await commands.SetModelError(modelCardId, e);
  }
}
