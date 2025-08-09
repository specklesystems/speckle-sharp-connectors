using System.Text;
using static Speckle.Converter.Navisworks.Helpers.PropertyHelpers;

namespace Speckle.Converter.Navisworks.ToSpeckle;

public static class RevitBuiltInCategoryExtractor
{
  private const int ANCESTOR_AND_SELF_COUNT = 4;

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

    // Skip adding if no mapping found (builtInCategory == convertedValue).
    // Doubles as a debug point for identifying unmapped categories to add to s_revitCatMap.
    if (builtInCategory == convertedValue)
    {
      return;
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

  // Mapping of Navisworks/Revit display category names (as reported by the Navisworks Revit importer)
  // to Revit BuiltInCategory constants. Keys are matched case-insensitively.
  //
  // This list is intended to reflect what Navisworks reports, not necessarily what the Revit
  // DirectShape API can assign on receipt. Revit receipt will ignore some categories
  // due to DirectShape limitations, in which case the receiver will handle fallback internally.
  //
  // Note: This is not const because Dictionary<T, U> is mutable; `static readonly` ensures
  // the reference cannot be replaced, but entries can be extended at runtime if needed.
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
  /// Attempts to map a Navisworks/Revit display category name to a Revit BuiltInCategory constant.
  /// Performs a fast lookup on the raw string first (case-insensitive),
  /// only normalizing and re-checking if leading/trailing or double whitespace is detected.
  /// </summary>
  private static string DisplayNameToRevitBuiltInCategory(string displayName)
  {
    if (string.IsNullOrWhiteSpace(displayName))
    {
      return displayName;
    }

    // Try raw key first — dictionary is OrdinalIgnoreCase.
    if (s_revitCatMap.TryGetValue(displayName, out var cat))
    {
      return cat;
    }

    // Normalize only if we detect issues; avoid allocations on clean strings.
    var norm = NormalizeKeyIfNeeded(displayName, out bool changed);
    return changed && s_revitCatMap.TryGetValue(norm, out cat) ? cat : displayName;
  }

  /// <summary>
  /// Returns the original string if it is already normalized.
  /// Normalization trims leading/trailing whitespace and collapses multiple
  /// consecutive whitespace characters into a single space.
  /// </summary>
  /// <param name="s">The input string to normalize.</param>
  /// <param name="changed">True if normalization modified the input; otherwise false.</param>
  private static string NormalizeKeyIfNeeded(string s, out bool changed)
  {
    changed = false;
    if (string.IsNullOrEmpty(s))
    {
      return string.Empty;
    }

    int len = s.Length;
    bool hasLeading = char.IsWhiteSpace(s[0]);
    bool hasTrailing = len > 1 && char.IsWhiteSpace(s[len - 1]);

    // Detect double/multiple whitespace without allocating
    bool lastWs = false;
    for (int i = 0; i < len; i++)
    {
      char c = s[i];
      bool ws = char.IsWhiteSpace(c);
      if (ws && lastWs)
      {
        changed = true;
        break;
      }

      lastWs = ws;
    }

    if (!changed && !hasLeading && !hasTrailing)
    {
      return s; // already normalized
    }

    // Perform normalization
    var sb = new StringBuilder(len);
    lastWs = true; // treat leading whitespace as already written
    for (int i = 0; i < len; i++)
    {
      char c = s[i];
      if (char.IsWhiteSpace(c))
      {
        if (!lastWs)
        {
          sb.Append(' ');
          lastWs = true;
        }
      }
      else
      {
        sb.Append(c);
        lastWs = false;
      }
    }

    // Remove a trailing space if we ended on whitespace
    if (lastWs && sb.Length > 0 && sb[^1] == ' ')
    {
      sb.Length--;
    }

    changed = true;
    return sb.ToString();
  }
}
