using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Speckle.Connectors.Logging;

public sealed class LoggingActivityFactory : IDisposable
{
  private readonly ActivitySource _activitySource =
    new(Consts.TRACING_SOURCE, Consts.GetPackageVersion(Assembly.GetExecutingAssembly()));

  public LoggingActivity? Start(string? name = null, [CallerMemberName] string source = "")
  {
    //If you get a MissingManifestResourceException, Likely source or name is empty string, which is no good.
    var activity = _activitySource.StartActivity(name ?? source, ActivityKind.Client);
    if (activity is null)
    {
      return null;
    }
    return new LoggingActivity(activity);
  }

  public void Dispose() => _activitySource.Dispose();
}
