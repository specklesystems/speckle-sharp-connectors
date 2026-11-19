namespace Speckle.Converters.Plant3dShared.ToSpeckle;

/// <summary>
/// The kind of Plant 3D drawing, derived from the project part the drawing belongs to.
/// </summary>
public enum Plant3dDrawingType
{
  /// <summary>Ortho, Iso, Misc, or a drawing not belonging to a Plant project.</summary>
  Other = 0,
  PnId,
  Piping,
}

public static class Plant3dDrawingTypes
{
  /// <summary>
  /// Maps a Plant 3D project part name (e.g. from <c>PnPProjectUtils</c>) to a <see cref="Plant3dDrawingType"/>.
  /// Unknown, empty or unsupported parts (Ortho, Iso, Misc) map to <see cref="Plant3dDrawingType.Other"/>.
  /// </summary>
  public static Plant3dDrawingType FromProjectPart(string? projectPart) =>
    projectPart?.ToUpperInvariant() switch
    {
      "PNID" => Plant3dDrawingType.PnId,
      "PIPING" => Plant3dDrawingType.Piping,
      _ => Plant3dDrawingType.Other,
    };
}
