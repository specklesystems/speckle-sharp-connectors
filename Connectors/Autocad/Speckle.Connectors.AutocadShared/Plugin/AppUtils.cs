using Speckle.Connectors.Common;

namespace Speckle.Connectors.Autocad.Plugin;

public static class AppUtils
{
  public static Speckle.Sdk.Application App =>
#if CIVIL3D
    HostApplications.Civil3D;
#elif AUTOCAD
    HostApplications.AutoCAD;
#else
    throw new NotSupportedException();
#endif

  public static HostAppVersion Version =>
#if AUTOCAD2026 || CIVIL3D2026
    HostAppVersion.v2026;
#elif AUTOCAD2025 || CIVIL3D2025
    HostAppVersion.v2025;
#elif AUTOCAD2024 || CIVIL3D2024
    HostAppVersion.v2024;
#elif AUTOCAD2023|| CIVIL3D2023
    HostAppVersion.v2023;
#elif AUTOCAD2022 || CIVIL3D2022
    HostAppVersion.v2022;
#else
    throw new NotSupportedException();
#endif
}
