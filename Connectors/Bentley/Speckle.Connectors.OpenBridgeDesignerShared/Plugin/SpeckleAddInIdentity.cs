using Speckle.Connectors.Common;
using Speckle.Objects.Data;
using Speckle.Sdk.Models;
using SpeckleApplication = Speckle.Sdk.Application;

namespace Speckle.Connectors.MicroStation.Plugin;

/// <summary>
/// Per-product identity for OpenBridge Designer / OpenBridge Modeler 2025. See
/// <c>MicroStationCONNECT</c>'s copy for the migration note about <c>OpenBridgeDataObject</c>.
/// </summary>
internal static class SpeckleAddInIdentity
{
  public const string MDL_TASK_ID = "SpeckleOpenBridgeDesigner";
  public const string PRODUCT_SLUG = "openbridgedesigner";

  public const HostAppVersion VERSION = HostAppVersion.v2025;

  // TODO: replace with HostApplications.OpenBridge once added to Speckle.Connectors.Common.
  public static readonly SpeckleApplication HostApp = HostApplications.MicroStation;

  public static DataObject CreateDataObject(
    string typeName,
    List<Base> displayValue,
    Dictionary<string, object?> properties,
    string units,
    string applicationId
  )
  {
    properties["bentleyProduct"] = "OpenBridgeDesigner";
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
