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
#if NAVIS2024
    HostAppVersion.v2024;
#else
    throw new NotSupportedException();
#endif
}
