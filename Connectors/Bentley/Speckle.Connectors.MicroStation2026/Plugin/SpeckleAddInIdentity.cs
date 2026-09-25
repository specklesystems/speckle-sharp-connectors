using Speckle.Connectors.Common;
using SpeckleApplication = Speckle.Sdk.Application;

namespace Speckle.Connectors.MicroStation.Plugin;

/// <summary>
/// Per-product identity values consumed by the source-shared <see cref="SpeckleAddIn"/>,
/// <c>MicroStationDocumentModelStore</c>, and the send pipeline. Each Bentley host application
/// (MicroStation, OpenRoads Designer, OpenBridge Designer) supplies its own copy of this static
/// class so each product DLL stamps in its own MdlTaskID, slug, host app and version at compile time.
/// </summary>
internal static class SpeckleAddInIdentity
{
  // Used by [AddIn(MdlTaskID = …)] — must be unique across all add-ins loaded into a host session.
  public const string MDL_TASK_ID = "SpeckleMicroStation";

  // Per-product subfolder name under %AppData%\Speckle\ for model card state isolation.
  public const string PRODUCT_SLUG = "microstation";

  public const HostAppVersion VERSION = HostAppVersion.v2026;

  public static readonly SpeckleApplication HostApp = HostApplications.MicroStation;
}
