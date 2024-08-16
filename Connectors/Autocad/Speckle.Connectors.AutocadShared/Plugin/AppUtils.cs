using Speckle.Sdk.Host;

namespace Speckle.Connectors.Autocad.Plugin;

public static class AppUtils
{
  public static HostApplication App =>
#if CIVIL3D
    HostApplications.Civil3D;
#elif AUTOCAD
    HostApplications.AutoCAD;
#else
    throw new NotImplementedException();
#endif

  public static HostAppVersion Version =>
#if CIVIL3D2024
    HostAppVersion.v2024;
#elif AUTOCAD2025
    HostAppVersion.v2025;
#elif AUTOCAD2024
    HostAppVersion.v2024;
#elif AUTOCAD2023
    HostAppVersion.v2023;
#elif AUTOCAD2022
    HostAppVersion.v2022;
#else
    throw new NotImplementedException();
#endif
}
