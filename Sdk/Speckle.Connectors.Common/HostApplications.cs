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
    Other = new("Other", "other");
}
