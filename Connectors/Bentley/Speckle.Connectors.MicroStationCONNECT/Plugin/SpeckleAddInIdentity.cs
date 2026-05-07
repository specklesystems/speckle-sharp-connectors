using Speckle.Connectors.Common;
using Speckle.Objects.Data;
using Speckle.Sdk.Models;
using SpeckleApplication = Speckle.Sdk.Application;

namespace Speckle.Connectors.MicroStation.Plugin;

/// <summary>
/// Per-product identity values consumed by the source-shared <see cref="SpeckleAddIn"/>,
/// <c>MicroStationDocumentModelStore</c>, and <c>MicroStationRootObjectBuilder</c>. Each Bentley
/// host application (MicroStation, OpenRoads Designer, OpenBridge Designer) supplies its own
/// copy of this static class so each product DLL stamps in its own MdlTaskID, slug, host app,
/// version, and DataObject wrapper at compile time.
/// </summary>
internal static class SpeckleAddInIdentity
{
  // Used by [AddIn(MdlTaskID = …)] — must be unique across all add-ins loaded into a host session.
  public const string MDL_TASK_ID = "SpeckleMicroStation";

  // Per-product subfolder name under %AppData%\Speckle\ for model card state isolation.
  public const string PRODUCT_SLUG = "microstation";

  public const HostAppVersion VERSION = HostAppVersion.v2026;

  public static readonly SpeckleApplication HostApp = HostApplications.MicroStation;

  /// <summary>
  /// Wraps the geometric conversion of a DGN element into a Speckle <see cref="DataObject"/>.
  /// </summary>
  /// <remarks>
  /// Uses the base <see cref="DataObject"/> for now because Speckle.Objects 3.17.0 (the published
  /// NuGet) does not yet contain the per-product subtypes <c>MicroStationDataObject</c> /
  /// <c>OpenRoadsDataObject</c> / <c>OpenBridgeDataObject</c> we added to <c>speckle-sharp-sdk</c>.
  /// Once a 3.18+ release ships with those types, swap the <c>new DataObject { … }</c> body of
  /// each per-product copy of this method to <c>new MicroStationDataObject { type = typeName, … }</c>
  /// (etc) for stronger typing. The product discriminator is meanwhile carried in
  /// <c>properties["bentleyProduct"]</c>.
  /// </remarks>
  public static DataObject CreateDataObject(
    string typeName,
    List<Base> displayValue,
    Dictionary<string, object?> properties,
    string units,
    string applicationId
  )
  {
    properties["bentleyProduct"] = "MicroStation";
    properties["bentleyType"] = typeName;
    return new DataObject
    {
      name = typeName,
      displayValue = displayValue,
      properties = properties,
      applicationId = applicationId,
      ["units"] = units,
    };
  }
}
