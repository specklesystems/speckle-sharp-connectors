using Speckle.Connectors.Common;

namespace Speckle.Connector.Navisworks.Plugin.Tools;

public static class SpeckleV3Tool
{
  public const string DEVELOPER_ID = "Speckle";
  public const string COMMAND = "Speckle_Launch";
  public const string PLUGIN = "SpeckleUI3";
  public const string PLUGIN_ID = "SpeckleNavisworksNextGen";
  public const string DISPLAY_NAME = "Speckle";
  public const string RIBBON_TAB_ID = "Speckle";
  public const string RIBBON_TAB_DISPLAY_NAME = "Speckle";
  public const string RIBBON_STRINGS = "NavisworksRibbon.name";
  public const string PLUGIN_SUFFIX = ".Speckle";

  public static Sdk.Application App =>
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
