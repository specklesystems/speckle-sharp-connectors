using Speckle.Sdk.Host;

namespace Speckle.Connector.Navisworks.NavisPlugin;

public static class AppUtils
{
  public static HostApplication App =>
#if NAVIS
    HostApplications.Navisworks;
#else
    throw new NotSupportedException();
#endif
  public static HostAppVersion Version =>
#if NAVIS2020
    HostAppVersion.v2020;
#elif NAVIS2021
    HostAppVersion.v2021;
#elif NAVIS2022
    HostAppVersion.v2022;
#elif NAVIS2023
    HostAppVersion.v2023;
#elif NAVIS2024
    HostAppVersion.v2024;
#elif NAVIS2025
    HostAppVersion.v2025;
#elif NAVIS2026
    HostAppVersion.v2026;
#else
    throw new NotSupportedException("This version is not supported");
#endif
}
