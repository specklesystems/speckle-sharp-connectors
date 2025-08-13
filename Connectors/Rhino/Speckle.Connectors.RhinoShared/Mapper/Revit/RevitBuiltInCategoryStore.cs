// TODO: Where should this live? Speckle.Connectors.Common.Interop??

namespace Speckle.Connectors.Rhino.Mapper.Revit;

/// <summary>
/// Represents a category option for the dropdown in the UI.
/// </summary>
/// <param name="Value">The Revit category enum name (e.g., "OST_Walls")</param>
/// <param name="Label">Human-readable name for the UI (e.g., "Walls")</param>
public record CategoryOption(string Value, string Label);

/// <summary>
/// Currently hardcoded Revit BuiltInCategories for the Interop Lite mapper.
/// This provides a select list of commonly used categories for object mapping
/// from Rhino to Revit.
///
/// NOTE: Currently located in Rhino codebase for Rhino-to-Revit POC use case.
/// When we extend to other connectors implementing a RevitMapper interface,
/// this should be moved to a shared location (e.g., Speckle.Connectors.Common
/// or Speckle.Converters.RevitShared).
/// </summary>
public static class RevitBuiltInCategoryStore
{
  /// <summary>
  /// Dictionary mapping Revit BuiltInCategory enum names to human-readable labels.
  /// Key: BuiltInCategory enum name (e.g., "OST_Walls")
  /// Value: Human-readable label (e.g., "Walls")
  /// </summary>
  public static readonly CategoryOption[] Categories =
  [
    // INFRASTRUCTURE
    new("OST_BridgeAbutments", "Bridge Abutments"),
    new("OST_BridgeFraming", "Bridge Framing"),
    new("OST_BridgeBearings", "Bridge Bearings"),
    new("OST_BridgeCables", "Bridge Cables"),
    new("OST_BridgeDecks", "Bridge Decks"),
    new("OST_ExpansionJoints", "Expansion Joints"),
    new("OST_BridgePiers", "Bridge Piers"),
    new("OST_VibrationManagement", "Vibration Management"),
    // ELECTRICAL
    new("OST_AudioVisualDevices", "Audio Visual Devices"),
    new("OST_CableTray", "Cable Tray"),
    new("OST_CableTrayFitting", "Cable Tray Fittings"),
    new("OST_CommunicationDevices", "Communication Devices"),
    new("OST_Conduit", "Conduits"),
    new("OST_ConduitFitting", "Conduit Fittings"),
    new("OST_DataDevices", "Data Devices"),
    new("OST_ElectricalEquipment", "Electrical Equipment"),
    new("OST_ElectricalFixtures", "Electrical Fixtures"),
    new("OST_FireAlarmDevices", "Fire Alarm Devices"),
    new("OST_LightingDevices", "Lighting Devices"),
    new("OST_LightingFixtures", "Lighting Fixtures"),
    new("OST_NurseCallDevices", "Nurse Call Devices"),
    new("OST_SecurityDevices", "Security Devices"),
    new("OST_TelephoneDevices", "Telephone Devices"),
    // ARCHITECTURAL
    new("OST_Casework", "Casework"),
    new("OST_Ceilings", "Ceilings"),
    new("OST_Columns", "Columns"),
    new("OST_CurtainWallMullions", "Curtain Wall Mullions"),
    new("OST_CurtainWallPanels", "Curtain Panels"),
    // new("OST_Curtain_Systems", "Curtain Systems"), excluded as part of CNX-2299
    new("OST_Doors", "Doors"),
    new("OST_Entourage", "Entourage"),
    new("OST_Floors", "Floors"),
    new("OST_FoodServiceEquipment", "Food Service Equipment"),
    new("OST_Furniture", "Furniture"),
    new("OST_FurnitureSystems", "Furniture Systems"),
    new("OST_Hardscape", "Hardscape"),
    new("OST_Parking", "Parking"),
    new("OST_Planting", "Planting"),
    new("OST_Railings", "Railings"),
    new("OST_Ramps", "Ramps"),
    new("OST_Roads", "Roads"),
    new("OST_Roofs", "Roofs"),
    new("OST_Site", "Site"),
    new("OST_SpecialityEquipment", "Speciality Equipment"),
    new("OST_Stairs", "Stairs"),
    new("OST_Topography", "Topography"),
    new("OST_Toposolid", "Toposolid"),
    new("OST_Walls", "Walls"),
    new("OST_Windows", "Windows"),
    // STRUCTURAL
    // new("OST_StructuralColumns", "Structural Columns"), excluded as part of CNX-2299
    new("OST_StructuralConnections", "Structural Connections"),
    new("OST_StructuralFoundation", "Structural Foundations"),
    new("OST_StructuralFraming", "Structural Framing"),
    new("OST_StructuralFramingSystem", "Structural Beam Systems"),
    new("OST_StructuralFabricAreas", "Structural Fabric Areas"),
    new("OST_Rebar", "Rebar"),
    new("OST_StructuralStiffener", "Structural Stiffeners"),
    new("OST_StructuralTendons", "Structural Tendons"),
    new("OST_StructuralTruss", "Structural Trusses"),
    // MECHANICAL
    new("OST_DuctAccessory", "Duct Accessories"),
    new("OST_DuctCurves", "Ducts"),
    new("OST_DuctFitting", "Duct Fittings"),
    new("OST_DuctSystem", "Duct Systems"),
    new("OST_MechanicalEquipment", "Mechanical Equipment"),
    new("OST_PlumbingEquipment", "Plumbing Equipment"),
    new("OST_PlumbingFixtures", "Plumbing Fixtures"),
    // PIPING
    new("OST_PipeAccessory", "Pipe Accessories"),
    new("OST_PipeCurves", "Pipes"),
    new("OST_PipeFitting", "Pipe Fittings"),
    new("OST_Sprinklers", "Sprinklers"),
    // GENERAL/MULTI-DISCIPLINE
    new("OST_FireProtection", "Fire Protection"),
    new("OST_GenericModel", "Generic Models"),
    new("OST_Lines", "Lines"),
    new("OST_Mass", "Mass"),
    new("OST_MedicalEquipment", "Medical Equipment"),
    new("OST_Parts", "Parts"),
    new("OST_Signage", "Signage"),
    new("OST_TemporaryStructure", "Temporary Structures"),
    new("OST_VerticalCirculation", "Vertical Circulation")
  ];

  /// <summary>
  /// Gets the human-readable label for a category value.
  /// </summary>
  /// <param name="categoryValue">The category enum name (e.g., "OST_Walls")</param>
  /// <returns>Human-readable label (e.g., "Walls") or the original value if not found</returns>
  public static string GetLabel(string categoryValue)
  {
    var category = Categories.FirstOrDefault(c => c.Value == categoryValue);
    return category?.Label ?? categoryValue;
  }
}
