namespace Speckle.Converters.Autocad;

public enum AutocadModelPlacement
{
  DrawingWcs,
  CurrentUcs,
  GridCoordinates,
}

public sealed record AutocadModelProperty(object? Value, string? Units = null);

public record AutocadConversionSettings(
  Document Document,
  AG.Matrix3d? ReferencePointTransform,
  string SpeckleUnits,
  bool ApplyTransform = false,
  string ModelPlacementSource = "drawingWcs",
  IReadOnlyDictionary<string, AG.Matrix3d>? ModelPlacementOptions = null,
  IReadOnlyDictionary<string, AutocadModelProperty>? CoordinateMetadata = null
);
