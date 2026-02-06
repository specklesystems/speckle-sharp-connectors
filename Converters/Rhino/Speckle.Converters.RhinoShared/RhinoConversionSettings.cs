using Rhino;

namespace Speckle.Converters.Rhino;

/// <summary>
/// Represents the settings used for Rhino and Grasshopper conversions.
/// </summary>
public record RhinoConversionSettings(
  RhinoDoc Document,
  string SpeckleUnits,
  bool AddVisualizationProperties,
  bool ConvertMeshesToBreps = false
);
