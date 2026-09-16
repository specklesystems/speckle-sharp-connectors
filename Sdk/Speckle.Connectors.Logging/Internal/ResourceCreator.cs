using System.Diagnostics;
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
    Dictionary<string, object> resourceAttributes = new()
    {
      { Consts.DEPLOYMENT_ENVIRONMENT, GetDeploymentEnvironment().ToLowerInvariant() },
      { Consts.SERVICE_NAME, applicationAndVersion },
      { Consts.SERVICE_SLUG, slug },
      { Consts.OS_NAME, Environment.OSVersion.ToString() },
      { Consts.OS_VERSION, Environment.OSVersion.VersionString },
      { Consts.OS_TYPE, GetOsType() },
      { Consts.OS_DESCRIPTION, RuntimeInformation.OSDescription },
      { Consts.HOST_ARCH, GetHostArch() },
      { Consts.HOST_ID, GetOrCreateMachineGuid().ToString("D") },
#if NET5_0_OR_GREATER
      { Consts.PROCESS_PID, Environment.ProcessId.ToString() },
#endif
      { Consts.PROCESS_CREATION_TIME, Process.GetCurrentProcess().StartTime.ToString("O") },
      { Consts.RUNTIME_NAME, ".NET" },
      { Consts.RUNTIME_VERSION, RuntimeInformation.FrameworkDescription },
#if NET9_0_OR_GREATER
      { Consts.MEMORY_WORKING_SET, Environment.WorkingSet },
#endif
      { Consts.CPU_COUNT, Environment.ProcessorCount },
      { Consts.SESSION_ID, Consts.StaticSessionId },
    };
    return ResourceBuilder
      .CreateEmpty()
      .AddTelemetrySdk()
      .AddService(serviceName: serviceName, serviceVersion: connectorVersion, serviceInstanceId: Consts.StaticSessionId)
      .AddAttributes(resourceAttributes);
  }

  private static string GetDeploymentEnvironment()
  {
#if DEBUG || LOCAL
    return "Development";
#else
    return Environment.GetEnvironmentVariable("SPECKLE_DOTNET_ENVIRONMENT")
      ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
      ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
      ?? "Production";
#endif
  }

  private static string GetHostArch()
  {
    // Following Semantic conventions 1.44.0
    return RuntimeInformation.OSArchitecture switch
    {
      Architecture.X86 => "x86",
      Architecture.X64 => "amd64",
      Architecture.Arm => "arm32",
      Architecture.Arm64 => "arm64",
#if NET5_0_OR_GREATER
      Architecture.Ppc64le => "ppc64",
      Architecture.S390x => "s390x",
#endif
      _ => RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
    };
  }

  private static string GetOsType()
  {
    // Following Semantic conventions 1.44.0
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
      return "windows";
    }

    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
    {
      return "darwin";
    }

    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    {
      return "linux";
    }

#if NET5_0_OR_GREATER
    if (RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD))
    {
      return "freebsd";
    }
#endif
    return RuntimeInformation.OSDescription;
  }

  public static Guid GetOrCreateMachineGuid()
  {
    string filePath = Path.Combine(SpecklePathProvider.UserSpeckleFolderPath, "MachineGUID");
    if (File.Exists(filePath))
    {
      var existing = File.ReadAllText(filePath).Trim();
      if (Guid.TryParse(existing, out var guid))
      {
        return guid;
      }
    }
    var newGuid = Guid.NewGuid();

    var directory = Path.GetDirectoryName(filePath);
    if (!string.IsNullOrEmpty(directory))
    {
      Directory.CreateDirectory(directory);
    }

    File.WriteAllText(filePath, newGuid.ToString("D"));
    return newGuid;
  }
}
