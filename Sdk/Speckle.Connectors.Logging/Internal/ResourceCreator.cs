using System.Runtime.InteropServices;
using OpenTelemetry.Resources;

namespace Speckle.Connectors.Logging.Internal;

internal static class ResourceCreator
{
  internal static ResourceBuilder Create(string applicationAndVersion, string slug, string connectorVersion) =>
    ResourceBuilder
      .CreateEmpty()
      .AddService(serviceName: Consts.TRACING_SOURCE, serviceVersion: connectorVersion)
      .AddAttributes(
        [
          new(Consts.SERVICE_NAME, applicationAndVersion),
          new(Consts.SERVICE_SLUG, slug),
          new(Consts.OS_NAME, Environment.OSVersion.ToString()),
          new(Consts.OS_TYPE, RuntimeInformation.ProcessArchitecture.ToString()),
          new(Consts.OS_SLUG, DetermineHostOsSlug()),
          new(Consts.RUNTIME_NAME, RuntimeInformation.FrameworkDescription),
          new(Consts.RUNTIME_SESSION_ID, Consts.StaticSessionId),
        ]
      );

  private static string DetermineHostOsSlug()
  {
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
      return "Windows";
    }

    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
    {
      return "MacOS";
    }

    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    {
      return "Linux";
    }

    return RuntimeInformation.OSDescription;
  }
}
