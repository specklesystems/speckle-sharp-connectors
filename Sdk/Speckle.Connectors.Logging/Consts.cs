using System.Reflection;

namespace Speckle.Connectors.Logging;

public static class Consts
{
  public const string DEPLOYMENT_ENVIRONMENT = "deployment.environment.name"; // Semantic conventions 1.44.0
  public const string SERVICE_NAME = "connector.name";
  public const string SERVICE_SLUG = "connector.slug";

  public const string OS_NAME = "os.name"; // Semantic conventions 1.44.0
  public const string OS_VERSION = "os.version"; // Semantic conventions 1.44.0
  public const string OS_TYPE = "os.type"; // Semantic conventions 1.44.0
  public const string OS_DESCRIPTION = "os.description"; // Semantic conventions 1.44.0

  public const string HOST_ARCH = "host.arch"; // Semantic conventions 1.44.0
  public const string HOST_ID = "host.id"; // Semantic conventions 1.44.0

  public const string SESSION_ID = "session.id"; // Semantic conventions 1.44.0

  public const string PROCESS_CREATION_TIME = "process.creation.time"; // Semantic conventions 1.44.0
  public const string PROCESS_PID = "process.pid"; // Semantic conventions 1.44.0

  public const string RUNTIME_NAME = "process.runtime.name"; // Semantic conventions 1.44.0
  public const string RUNTIME_VERSION = "process.runtime.version"; // Semantic conventions 1.44.0

  public const string CPU_COUNT = "dotnet.process.cpu.count"; // Semantic conventions 1.44.0
  public const string MEMORY_WORKING_SET = "dotnet.process.memory.working_set"; // Semantic conventions 1.44.0

  public const string USER_ID = "user.id"; // Semantic conventions 1.44.0
  public const string USER_DISTINCT_ID = "user.distinctId";
  public const string USER_SERVER_URL = "user.server_url";
  public const string TRACING_SOURCE = "connector";

  /// <summary>
  /// A random GUID for adding to the logging context to correlate the <c>service.instance.id</c>
  /// </summary>
  public static readonly string StaticSessionId = Guid.NewGuid().ToString();

  public static string GetPackageVersion(Assembly assembly)
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
      return string.Empty;
    }

    var indexOfPlusSign = informationalVersion.IndexOf('+');
#pragma warning disable IDE0057
    return indexOfPlusSign > 0 ? informationalVersion.Substring(0, indexOfPlusSign) : informationalVersion;
#pragma warning restore IDE0057
  }
}
