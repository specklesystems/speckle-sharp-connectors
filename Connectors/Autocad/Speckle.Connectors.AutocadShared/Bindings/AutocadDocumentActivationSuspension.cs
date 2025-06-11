using Microsoft.Extensions.Logging;

namespace Speckle.Connectors.Autocad.Bindings;

public interface IAutocadDocumentActivationSuspension : IDisposable
{
  IDisposable Suspend();
}

public sealed class AutocadDocumentActivationSuspension(ILogger<AutocadDocumentActivationSuspension> logger)
  : IAutocadDocumentActivationSuspension
{
  private bool _suspended;

  public IDisposable Suspend()
  {
    try
    {
      Application.DocumentManager.DocumentActivationEnabled = false;
    }
#pragma warning disable CA1031
    catch (Exception ex)
#pragma warning restore CA1031
    {
      logger.LogError(ex, "Failed to enable document activation.");
      _suspended = false;
      return this;
    }
    _suspended = true;
    return this;
  }

  public void Dispose()
  {
    if (_suspended)
    {
      try
      {
        Application.DocumentManager.DocumentActivationEnabled = true;
      }
#pragma warning disable CA1031
      catch (Exception ex)
#pragma warning restore CA1031
      {
        logger.LogError(ex, "Failed to disable document activation.");
      }
    }
  }
}
