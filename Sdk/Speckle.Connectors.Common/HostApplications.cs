﻿using System.Text.RegularExpressions;
using Speckle.Sdk;

namespace Speckle.Connectors.Common;

public static class HostApplications
{
  public static string GetVersion(HostAppVersion version) => version.ToString().TrimStart('v');

  public static readonly Application Rhino = new("Rhino", "rhino"),
    Grasshopper = new("Grasshopper", "grasshopper"),
    Revit = new("Revit", "revit"),
    Dynamo = new("Dynamo", "dynamo"),
    Unity = new("Unity", "unity"),
    GSA = new("GSA", "gsa"),
    Civil = new("Civil 3D", "civil3d"),
    Civil3D = new("Civil 3D", "civil3d"),
    AutoCAD = new("AutoCAD", "autocad"),
    MicroStation = new("MicroStation", "microstation"),
    OpenRoads = new("OpenRoads", "openroads"),
    OpenRail = new("OpenRail", "openrail"),
    OpenBuildings = new("OpenBuildings", "openbuildings"),
    ETABS = new("ETABS", "etabs"),
    SAP2000 = new("SAP2000", "sap2000"),
    CSiBridge = new("CSiBridge", "csibridge"),
    SAFE = new("SAFE", "safe"),
    TeklaStructures = new("Tekla Structures", "teklastructures"),
    Dxf = new("DXF Converter", "dxf"),
    Excel = new("Excel", "excel"),
    Unreal = new("Unreal", "unreal"),
    PowerBI = new("Power BI", "powerbi"),
    Blender = new("Blender", "blender"),
    QGIS = new("QGIS", "qgis"),
    ArcGIS = new("ArcGIS", "arcgis"),
    SketchUp = new("SketchUp", "sketchup"),
    Archicad = new("Archicad", "archicad"),
    TopSolid = new("TopSolid", "topsolid"),
    Python = new("Python", "python"),
    NET = new(".NET", "net"),
    Navisworks = new("Navisworks", "navisworks"),
    AdvanceSteel = new("Advance Steel", "advancesteel"),
    Other = new("Other", "other"),
    RhinoImporter = new("RhinoImporter", "rhinoimporter");

  /// <summary>
  /// Gets a slug from a host application name and version.
  /// </summary>
  /// <param name="appName">Application name with its version, e.g., "Rhino 7", "Revit 2024".</param>
  /// <returns>Slug string.</returns>
  public static string GetSlugFromHostAppNameAndVersion(string appName)
  {
    if (string.IsNullOrWhiteSpace(appName))
    {
      return "other";
    }

    // Remove whitespace and convert to lowercase
    appName = Regex.Replace(appName.ToLowerInvariant(), @"\s+", "");

    var keywords = new List<string>
    {
      "dynamo",
      "revit",
      "autocad",
      "civil",
      "rhino",
      "grasshopper",
      "unity",
      "gsa",
      "microstation",
      "openroads",
      "openrail",
      "openbuildings",
      "etabs",
      "sap",
      "csibridge",
      "safe",
      "teklastructures",
      "dxf",
      "excel",
      "unreal",
      "powerbi",
      "blender",
      "qgis",
      "arcgis",
      "sketchup",
      "archicad",
      "topsolid",
      "python",
      "net",
      "navisworks",
      "advancesteel"
    };

    foreach (var keyword in keywords)
    {
      if (appName.Contains(keyword))
      {
        return keyword;
      }
    }

    return appName;
  }
}
