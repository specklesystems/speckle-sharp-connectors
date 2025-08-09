using System.Text;
using static Speckle.Converter.Navisworks.Helpers.PropertyHelpers;

namespace Speckle.Converter.Navisworks.ToSpeckle;

public static class RevitBuiltInCategoryExtractor
{
  private const int ANCESTOR_AND_SELF_COUNT = 4;
  private const string? UNSUPPORTED_ON_LOAD = null;

  /// <summary>
  /// Searches modelItem hierarchy for Revit category and adds mapped built-in category to the dictionary
  /// </summary>
  internal static void AddRevitCategoryFromHierarchy(
    NAV.ModelItem modelItem,
    Dictionary<string, object?> propertyDictionary
  )
  {
    var categoryValue = FindRevitCategoryInHierarchy(modelItem);
    if (categoryValue == null)
    {
      return;
    }
    var convertedValue = ConvertPropertyValue(categoryValue, "")?.ToString() ?? string.Empty;
    var builtInCategory = DisplayNameToRevitBuiltInCategory(convertedValue);

    if (builtInCategory == convertedValue)
    {
      // If the category is not mapped, we don't add it to the dictionary
      // return;
    }

    AddPropertyIfNotNullOrEmpty(propertyDictionary, "builtInCategory", builtInCategory);
  }



  /// <summary>
  /// Finds the Revit category in the hierarchy of modelItem.
  /// Early exit if the category is found.
  /// </summary>
  private static NAV.VariantData? FindRevitCategoryInHierarchy(NAV.ModelItem modelItem)
  {
    var current = modelItem;
    for (int i = 0; i < ANCESTOR_AND_SELF_COUNT && current != null; i++, current = current.Parent)
    {
      var categoryValue = current
        .PropertyCategories.FindPropertyByName("LcRevitData_Element", "LcRevitPropertyElementCategory")
        ?.Value;
      if (categoryValue != null)
      {
        return categoryValue;
      }
    }
    return null;
  }

  private static readonly Dictionary<string, string> _revittCatMap = new Dictionary<string, string>(
    StringComparer.OrdinalIgnoreCase
  )
  {
    ["Walls"] = "OST_Walls",
    ["Floors"] = "OST_Floors",
    ["Stairs"] = "OST_Stairs",
    ["Supports"] = "OST_Stairs",
    ["Runs"] = "OST_Stairs",
    ["Doors"] = "OST_Doors",
    ["Windows"] = "OST_Windows",
    ["Columns"] = "OST_Columns",
    ["Casework"] = "OST_Casework",
    ["Ceilings"] = "OST_Ceilings",
    ["Curtain Panels"] = "OST_CurtainWallPanels",
    ["Curtain Wall Mullions"] = "OST_CurtainWallMullions",
    ["Roofs"] = "OST_Roofs",
    ["Air Terminals"] = "OST_DuctTerminal",
    ["Structural Connections"] = "OST_StructConnections",
    ["Structural Foundations"] = "OST_StructuralFoundation",
    ["Structural Columns"] = "OST_StructuralColumns",
    ["Structural Framing"] = "OST_StructuralFraming",
    ["Conduit Fittings"] = "OST_ConduitFitting",
    ["Conduits"] = "OST_Conduit",
    ["Electrical Equipment"] = "OST_ElectricalEquipment",
    ["Electrical Fixtures"] = "OST_ElectricalFixtures",
    ["Generic Models"] = "OST_GenericModel",
    ["Handrails"] = "OST_RailingHandRail",
    ["Lighting Fixtures"] = "OST_LightingFixtures",
    ["Slab Edges"] = "OST_EdgeSlab",
    ["Parking"] = "OST_Parking",
    ["Railings"] = "OST_StairsRailing",
    ["Rooms"] = "OST_Rooms",
    ["Site"] = "OST_Site",
    ["Specialty Equipment"] = "OST_SpecialityEquipment",
    ["Landings"] = "OST_StairsLandings",
    ["Vertical Circulation"] = "OST_VerticalCirculation",
    ["Food Service Equipment"] = "OST_FoodServiceEquipment",
    ["Furniture"] = "OST_Furniture",
    ["Planting"] = "OST_Planting",
    ["Plumbing Fixtures"] = "OST_PlumbingFixtures",
    ["Wall Sweeps"] = "OST_Cornices",
    ["Hardscape"] = "OST_Hardscape",
    ["Ramps"] = "OST_Ramps",
    ["Entourage"] = "OST_Entourage",
    ["<Space Separation>"] = "OST_MEPSpaceSeparationLines",
    ["<Room Separation>"] = "OST_RoomSeparationLines",
    ["Levels"] = "OST_Levels",
    ["Lines"] = "OST_Lines",
    ["Center line"] = "OST_CenterLines",
    ["Center Line"] = "OST_CenterLines",
    ["Duct Fittings"] = "OST_DuctFitting",
    ["Ducts"] = "OST_DuctCurves",
    ["Mechanical Equipment"] = "OST_MechanicalEquipment",
    ["Flex Ducts"] = "OST_FlexDuctCurves",
    ["Plumbing Equipment"] = "OST_PlumbingEquipment",
    ["Pipe Accessories"] = "OST_PipeAccessory",
    ["Pipe Fittings"] = "OST_PipeFitting",
    ["Pipes"] = "OST_PipeCurves",
    ["Toposolid"] = "OST_Toposolid",
    ["Boundary Conditions"] = "OST_BoundaryConditions",
    ["Fascias"] = "OST_Fascia",
    ["Structural Loads"] = "OST_Loads",
    ["Structural Rebar"] = "OST_Rebar",
    ["Roof Soffits"] = "OST_RoofSoffit",
    ["Structural Fabric Areas"] = "OST_FabricAreas",
    ["Structural Fabric Reinforcement"] = "OST_FabricReinforcement",
    ["Pipe Insulations"] = "OST_PipeInsulations",
    ["Lighting Devices"] = "OST_LightingDevices",
    ["Cable Tray Fittings"] = "OST_CableTrayFitting",
    ["Cable Trays"] = "OST_CableTray",
    ["Data Devices"] = "OST_DataDevices",
    ["Duct Accessories"] = "OST_DuctAccessory",
    ["Flex Pipes"] = "OST_FlexPipeCurves",
    ["Communication Devices"] = "OST_CommunicationDevices",
    ["Conduit Accessories"] = "OST_ConduitAccessory"
  };

  private static string NormalizeKey(string s)
  {
    if (string.IsNullOrEmpty(s))
    {
      return string.Empty;
    }

    s = s.Trim();
    var sb = new StringBuilder(s.Length);
    bool lastWasSpace = false;
    foreach (char c in s)
    {
      if (char.IsWhiteSpace(c))
      {
        if (!lastWasSpace)
        {
          sb.Append(' ');
          lastWasSpace = true;
        }
      }
      else
      {
        sb.Append(c);
        lastWasSpace = false;
      }
    }
    return sb.ToString();
  }

  private static string DisplayNameToRevitBuiltInCategory(string displayName)
  {
    if (string.IsNullOrWhiteSpace(displayName))
    {
      return displayName;
    }

    var key = NormalizeKey(displayName);
    return _revittCatMap.TryGetValue(key, out var cat) ? cat : displayName;
  }
}
