using Speckle.Converter.Navisworks.Services;
using Speckle.InterfaceGenerator;

namespace Speckle.Converter.Navisworks.ToSpeckle;

[GenerateAutoInterface]
public class RevitBuiltInCategoryExtractor(IPropertyConverter converter) : IRevitBuiltInCategoryExtractor
{
  private const int ANCESTOR_AND_SELF_COUNT = 4; // It seems like this is the maximum depth found needed in practice
  private const string REVIT_CAT_GROUP = "LcRevitData_Element";
  private const string REVIT_CAT_NAME = "LcRevitPropertyElementCategory";
  internal const string DEFAULT_DICT_KEY = "builtInCategory";

  /// <summary>
  /// Attempts to map a Navisworks/Revit display category from the given model item or its ancestors
  /// to a known Revit built-in category constant (e.g., "OST_Walls").
  /// </summary>
  public bool TryGetBuiltInCategory(NAV.ModelItem item, out string mapped, int maxDepth = ANCESTOR_AND_SELF_COUNT)
  {
    mapped = string.Empty;

    // Find the category VariantData up the hierarchy
    var v = FindRevitCategoryInHierarchy(item, maxDepth);
    if (v is null)
    {
      return false;
    }

    converter.Reset();

    // Convert using per-object model units and current UI units
    var nameObj = converter.ConvertPropertyValue(v, item.Model.Units, item.DisplayName);
    var name = nameObj?.ToString();
    if (string.IsNullOrWhiteSpace(name))
    {
      return false;
    }

    name = name!.Trim();

    // Map display name to OST_* built-in category constant
    var builtInCategory = DisplayNameToRevitBuiltInCategory(name);
    if (string.Equals(builtInCategory, name, StringComparison.OrdinalIgnoreCase))
    {
      return false; // no mapping
    }

    mapped = builtInCategory;
    return true;
  }

  /// <summary>
  /// Walks up the model item hierarchy to find the first Revit element category property.
  /// </summary>
  private static NAV.VariantData? FindRevitCategoryInHierarchy(NAV.ModelItem modelItem, int maxDepth)
  {
    var current = modelItem;

    // Walk up the model item hierarchy to find the first matching Revit category property
    for (int i = 0; i < maxDepth && current != null; i++, current = current.Parent)
    {
      var val = current.PropertyCategories.FindPropertyByName(REVIT_CAT_GROUP, REVIT_CAT_NAME)?.Value;

      if (val != null)
      {
        return val;
      }
    }

    // No category property found in self or ancestors
    return null;
  }

  // Mapping of Navisworks/Revit display category names (from the importer)
  // to Revit BuiltInCategory constants. Case-insensitive.
  // Note: Some mapped categories are not assignable via Revit DirectShape;
  // the receiver will ignore them and apply its own fallback.
  private static readonly Dictionary<string, string> s_revitCatMap =
    new(StringComparer.OrdinalIgnoreCase)
    {
      // Architectural
      ["Walls"] = "OST_Walls",
      ["Floors"] = "OST_Floors",
      ["Roofs"] = "OST_Roofs",
      ["Ceilings"] = "OST_Ceilings",
      ["Doors"] = "OST_Doors",
      ["Windows"] = "OST_Windows",
      ["Curtain Panels"] = "OST_CurtainWallPanels",
      ["Curtain Wall Mullions"] = "OST_CurtainWallMullions",
      ["Wall Sweeps"] = "OST_Cornices",
      ["Hardscape"] = "OST_Hardscape",
      ["Site"] = "OST_Site",
      ["Parking"] = "OST_Parking",
      ["Toposolid"] = "OST_Toposolid",
      ["Levels"] = "OST_Levels",
      ["Lines"] = "OST_Lines",
      ["Center line"] = "OST_CenterLines",
      ["Center Line"] = "OST_CenterLines",

      // Stairs & Railings
      ["Stairs"] = "OST_Stairs",
      ["Supports"] = "OST_Stairs",
      ["Runs"] = "OST_Stairs",
      ["Railings"] = "OST_StairsRailing",
      ["Handrails"] = "OST_RailingHandRail",
      ["Landings"] = "OST_StairsLandings",
      ["Vertical Circulation"] = "OST_VerticalCirculation",

      // Structural
      ["Structural Connections"] = "OST_StructConnections",
      ["Structural Foundations"] = "OST_StructuralFoundation",
      ["Structural Columns"] = "OST_StructuralColumns",
      ["Structural Framing"] = "OST_StructuralFraming",
      ["Structural Loads"] = "OST_Loads",
      ["Structural Rebar"] = "OST_Rebar",
      ["Structural Fabric Areas"] = "OST_FabricAreas",
      ["Structural Fabric Reinforcement"] = "OST_FabricReinforcement",
      ["Boundary Conditions"] = "OST_BoundaryConditions",
      ["Slab Edges"] = "OST_EdgeSlab",
      ["Fascias"] = "OST_Fascia",
      ["Roof Soffits"] = "OST_RoofSoffit",

      // MEP - HVAC
      ["Air Terminals"] = "OST_DuctTerminal",
      ["Duct Fittings"] = "OST_DuctFitting",
      ["Ducts"] = "OST_DuctCurves",
      ["Flex Ducts"] = "OST_FlexDuctCurves",
      ["Duct Accessories"] = "OST_DuctAccessory",
      ["Mechanical Equipment"] = "OST_MechanicalEquipment",

      // MEP - Plumbing
      ["Plumbing Fixtures"] = "OST_PlumbingFixtures",
      ["Plumbing Equipment"] = "OST_PlumbingEquipment",
      ["Pipe Accessories"] = "OST_PipeAccessory",
      ["Pipe Fittings"] = "OST_PipeFitting",
      ["Pipes"] = "OST_PipeCurves",
      ["Flex Pipes"] = "OST_FlexPipeCurves",
      ["Pipe Insulations"] = "OST_PipeInsulations",

      // MEP - Electrical
      ["Electrical Equipment"] = "OST_ElectricalEquipment",
      ["Electrical Fixtures"] = "OST_ElectricalFixtures",
      ["Lighting Fixtures"] = "OST_LightingFixtures",
      ["Lighting Devices"] = "OST_LightingDevices",
      ["Data Devices"] = "OST_DataDevices",
      ["Communication Devices"] = "OST_CommunicationDevices",

      // MEP - Conduits & Cable Trays
      ["Conduit Fittings"] = "OST_ConduitFitting",
      ["Conduits"] = "OST_Conduit",
      ["Conduit Accessories"] = "OST_ConduitAccessory",
      ["Cable Tray Fittings"] = "OST_CableTrayFitting",
      ["Cable Trays"] = "OST_CableTray",

      // Equipment & Furniture
      ["Casework"] = "OST_Casework",
      ["Specialty Equipment"] = "OST_SpecialityEquipment",
      ["Food Service Equipment"] = "OST_FoodServiceEquipment",
      ["Furniture"] = "OST_Furniture",
      ["Generic Models"] = "OST_GenericModel",
      ["Planting"] = "OST_Planting",
      ["Entourage"] = "OST_Entourage",

      // Separations & Rooms
      ["<Space Separation>"] = "OST_MEPSpaceSeparationLines",
      ["<Room Separation>"] = "OST_RoomSeparationLines",
      ["Rooms"] = "OST_Rooms",

      // Misc
      ["Ramps"] = "OST_Ramps"
    };

  /// <summary>
  /// Maps a Navisworks/Revit display category name to a Revit BuiltInCategory.
  /// Assumes importer emits canonical names. Case-insensitive lookup.
  /// Returns the original name when no mapping exists.
  /// </summary>
  private static string DisplayNameToRevitBuiltInCategory(string displayName) =>
    string.IsNullOrEmpty(displayName)
      ? displayName
      : s_revitCatMap.TryGetValue(displayName, out var builtInCategory)
        ? builtInCategory
        : displayName;
}
