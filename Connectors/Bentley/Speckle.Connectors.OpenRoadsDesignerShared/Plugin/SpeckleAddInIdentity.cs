using Speckle.Connectors.Common;
using Speckle.Objects.Data;
using Speckle.Sdk.Models;
using SpeckleApplication = Speckle.Sdk.Application;

namespace Speckle.Connectors.MicroStation.Plugin;

/// <summary>
/// Per-product identity for OpenRoads Designer 2025. See <c>MicroStationCONNECT</c>'s copy of
/// this file for the migration note about <c>OpenRoadsDataObject</c>.
/// </summary>
internal static class SpeckleAddInIdentity
{
  public const string MDL_TASK_ID = "SpeckleOpenRoadsDesigner";
  public const string PRODUCT_SLUG = "openroadsdesigner";

  public const HostAppVersion VERSION = HostAppVersion.v2025;

  public static readonly SpeckleApplication HostApp = HostApplications.OpenRoads;

  public static DataObject CreateDataObject(
    string typeName,
    List<Base> displayValue,
    Dictionary<string, object?> properties,
    string units,
    string applicationId
  )
  {
    properties["bentleyProduct"] = "OpenRoadsDesigner";
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
