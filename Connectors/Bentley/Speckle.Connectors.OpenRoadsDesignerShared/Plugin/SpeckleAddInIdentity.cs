using Speckle.Connectors.Common;
using SpeckleApplication = Speckle.Sdk.Application;

namespace Speckle.Connectors.MicroStation.Plugin;

/// <summary>
/// Per-product identity values consumed by the source-shared <see cref="SpeckleAddIn"/>
/// and <c>MicroStationDocumentModelStore</c>. OpenRoads Designer is a MicroStation-platform
/// vertical product; it loads the same shared add-in code with its own identity.
/// </summary>
internal static class SpeckleAddInIdentity
{
  public const string MDL_TASK_ID = "SpeckleOpenRoadsDesigner";
  public const string PRODUCT_SLUG = "openroadsdesigner";

  public const HostAppVersion VERSION = HostAppVersion.v2025;

  public static readonly SpeckleApplication HostApp = HostApplications.OpenRoads;
}
