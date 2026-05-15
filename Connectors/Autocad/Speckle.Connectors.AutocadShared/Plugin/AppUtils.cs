using Speckle.Connectors.Common;

namespace Speckle.Connectors.Autocad.Plugin;

public static class AppUtils
{
  public static Speckle.Sdk.Application App =>
#if CIVIL3D
    HostApplications.Civil3D;
#elif PLANT3D
    HostApplications.Plant3D;
#elif AUTOCAD
    HostApplications.AutoCAD;
#else
    throw new NotSupportedException();
#error Compiler directives misconfigured or the autocad vertical is not supported
#endif

  public static HostAppVersion Version =>
#if AUTOCAD2027 || CIVIL3D2027 || PLANT3D2027
    HostAppVersion.v2027;
#elif AUTOCAD2026 || CIVIL3D2026 || PLANT3D2026
    HostAppVersion.v2026;
#elif AUTOCAD2025 || CIVIL3D2025 || PLANT3D2025
    HostAppVersion.v2025;
#elif AUTOCAD2024 || CIVIL3D2024 || PLANT3D2024
    HostAppVersion.v2024;
#elif AUTOCAD2023|| CIVIL3D2023
    HostAppVersion.v2023;
#else
    throw new NotSupportedException();
#error Compiler directives misconfigured or the autocad version is not supported
#endif
}
