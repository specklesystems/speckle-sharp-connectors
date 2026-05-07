using Speckle.Connectors.Common;
using SpeckleApplication = Speckle.Sdk.Application;

namespace Speckle.Connectors.MicroStation.Plugin;

/// <summary>
/// Per-product identity values consumed by the source-shared <see cref="SpeckleAddIn"/>
/// and <c>MicroStationDocumentModelStore</c>. OpenBridge Designer hosts a MicroStation-based
/// child app (OpenBridge Modeler); this connector loads into that host alongside MicroStation/OpenRoads.
/// HostApp falls back to <c>HostApplications.MicroStation</c> because Speckle.Connectors.Common
/// does not yet have a dedicated <c>OpenBridge</c> entry.
/// </summary>
internal static class SpeckleAddInIdentity
{
  public const string MDL_TASK_ID = "SpeckleOpenBridgeDesigner";
  public const string PRODUCT_SLUG = "openbridgedesigner";

  public const HostAppVersion VERSION = HostAppVersion.v2025;

  // TODO: replace with HostApplications.OpenBridge once added to Speckle.Connectors.Common.
  public static readonly SpeckleApplication HostApp = HostApplications.MicroStation;
}
