using Speckle.Converters.Autocad;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Plant3dShared.ToSpeckle.Geometry;

/// <summary>
/// Converts a top-level <see cref="ADB.Solid"/> (a legacy AutoCAD 2D filled solid with 3 or 4
/// coplanar corners, created by the SOLID command) to a <see cref="SOG.Mesh"/>.
/// <para>
/// Note: unlike <see cref="ADB.Face"/>, <see cref="ADB.Solid"/> sub-entities produced by
/// <c>Explode()</c> are not skipped in <see cref="Plant3dEntityToSpeckleConverter"/> — they
/// are converted directly here when encountered.
/// </para>
/// </summary>
[NameAndRankValue(typeof(ADB.Solid), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class SolidToSpeckleConverter(
  IReferencePointConverter referencePointConverter,
  IConverterSettingsStore<AutocadConversionSettings> settingsStore
) : IToSpeckleTopLevelConverter
{
  public Base Convert(object target) => Convert((ADB.Solid)target);

  public SOG.Mesh Convert(ADB.Solid target)
  {
    AG.Point3d p0 = target.GetPointAt(0);
    AG.Point3d p1 = target.GetPointAt(1);
    AG.Point3d p2 = target.GetPointAt(2);
    AG.Point3d p3 = target.GetPointAt(3);

    // ADB.Solid stores 4 corners; a triangle is represented by duplicating the third corner.
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
