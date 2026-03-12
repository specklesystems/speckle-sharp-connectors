using System.Runtime.InteropServices;
using OpenTelemetry.Resources;

namespace Speckle.Connectors.Logging.Internal;

internal static class ResourceCreator
{
  internal static ResourceBuilder Create(
    string serviceName,
    string applicationAndVersion,
    string slug,
    string connectorVersion
  )
  {
    string deploymentEnvironment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
    return ResourceBuilder
      .CreateEmpty()
      .AddService(serviceName: serviceName, serviceVersion: connectorVersion, serviceInstanceId: Consts.StaticSessionId)
      .AddAttributes(
        [
          new(Consts.DEPLOYMENT_ENVIRONMENT, deploymentEnvironment.ToLowerInvariant()),
          new(Consts.SERVICE_NAME, applicationAndVersion),
          new(Consts.SERVICE_SLUG, slug),
          new(Consts.OS_NAME, Environment.OSVersion.ToString()),
          new(Consts.OS_TYPE, RuntimeInformation.ProcessArchitecture.ToString()),
          new(Consts.OS_SLUG, DetermineHostOsSlug()),
          new(Consts.RUNTIME_NAME, ".NET"),
          new(Consts.RUNTIME_VERSION, RuntimeInformation.FrameworkDescription),
        ]
      );
  }

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
