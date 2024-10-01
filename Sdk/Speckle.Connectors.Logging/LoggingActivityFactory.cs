using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Speckle.Connectors.Logging;

public sealed class LoggingActivityFactory : IDisposable
{
  public const string TRACING_SOURCE = "speckle-connectors";
  private readonly ActivitySource? _activitySource =
    new(TRACING_SOURCE, GetPackageVersion(Assembly.GetExecutingAssembly()));
  
  private readonly Dictionary<string, object?> _tags = new();
  
  public void SetTag(string key, object? value) => _tags[key] = value;

  public LoggingActivity? Start(string? name = null, [CallerMemberName] string source = "")
  {
    //If you get a MissingManifestResourceException, Likely source or name is empty string, which is no good.
    var activity = _activitySource?.StartActivity(name: name ?? source, kind: ActivityKind.Client, tags: _tags);
    if (activity is null)
    {
      return null;
    }
    return new LoggingActivity(activity);
  }

  public void Dispose() => _activitySource?.Dispose();

  private static string GetPackageVersion(Assembly assembly)
  {
    // MinVer https://github.com/adamralph/minver?tab=readme-ov-file#version-numbers
    // together with Microsoft.SourceLink.GitHub https://github.com/dotnet/sourcelink
    // fills AssemblyInformationalVersionAttribute by
    // {majorVersion}.{minorVersion}.{patchVersion}.{pre-release label}.{pre-release version}.{gitHeight}+{Git SHA of current commit}
    // Ex: 1.5.0-alpha.1.40+807f703e1b4d9874a92bd86d9f2d4ebe5b5d52e4
    // The following parts are optional: pre-release label, pre-release version, git height, Git SHA of current commit
    // For package version, value of AssemblyInformationalVersionAttribute without commit hash is returned.

    var informationalVersion = assembly
      .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
      ?.InformationalVersion;
    if (informationalVersion is null)
    {
      return String.Empty;
    }

    var indexOfPlusSign = informationalVersion.IndexOf('+');
    return indexOfPlusSign > 0 ? informationalVersion[..indexOfPlusSign] : informationalVersion;
  }
}
