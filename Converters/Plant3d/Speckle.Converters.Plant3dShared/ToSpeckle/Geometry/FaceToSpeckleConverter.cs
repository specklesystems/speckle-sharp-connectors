using Speckle.Converters.Autocad;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Plant3dShared.ToSpeckle.Geometry;

/// <summary>
/// Converts a top-level <see cref="ADB.Face"/> (a legacy AutoCAD 3D face with 3 or 4 corners)
/// to a <see cref="SOG.Mesh"/>.
/// Note: <see cref="ADB.Face"/> sub-entities produced by <c>Explode()</c> are intentionally
/// skipped in <see cref="Plant3dEntityToSpeckleConverter"/> because the parent entity is already
/// converted to a Mesh by its own dedicated converter.
/// </summary>
[NameAndRankValue(typeof(ADB.Face), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class FaceToSpeckleConverter(
  IReferencePointConverter referencePointConverter,
  IConverterSettingsStore<AutocadConversionSettings> settingsStore
) : IToSpeckleTopLevelConverter
{
  public Base Convert(object target) => Convert((ADB.Face)target);

  public SOG.Mesh Convert(ADB.Face target)
  {
    AG.Point3d p0 = target.GetVertexAt(0);
    AG.Point3d p1 = target.GetVertexAt(1);
    AG.Point3d p2 = target.GetVertexAt(2);
    AG.Point3d p3 = target.GetVertexAt(3);

    // ADB.Face stores 4 corners, but a triangle is represented by duplicating the third corner.
    bool isTriangle = p2.IsEqualTo(p3);

    List<double> rawVertices = isTriangle
      ? [p0.X, p0.Y, p0.Z, p1.X, p1.Y, p1.Z, p2.X, p2.Y, p2.Z]
      : [p0.X, p0.Y, p0.Z, p1.X, p1.Y, p1.Z, p2.X, p2.Y, p2.Z, p3.X, p3.Y, p3.Z];

    List<int> faces = isTriangle ? [3, 0, 1, 2] : [4, 0, 1, 2, 3];

    return new SOG.Mesh()
    {
      vertices = referencePointConverter.ConvertWCSDoublesToExternalCoordinates(rawVertices),
      faces = faces,
      units = settingsStore.Current.SpeckleUnits,
    };
  }
}
