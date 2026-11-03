using Speckle.Converters.Autocad;
using Speckle.Converters.Common;

namespace Speckle.Converters.Plant3dShared.Helpers;

/// <summary>
/// Builds a <see cref="SOG.Mesh"/> from 3 or 4 WCS corners, as used by legacy
/// <see cref="ADB.Face"/> and <see cref="ADB.Solid"/> entities.
/// A triangle is represented by the last two corners being equal.
/// </summary>
/// <remarks>Corners must be supplied in WCS and in winding (loop) order.</remarks>
internal static class QuadMeshBuilder
{
  public static SOG.Mesh Build(
    AG.Point3d p0,
    AG.Point3d p1,
    AG.Point3d p2,
    AG.Point3d p3,
    IReferencePointConverter referencePointConverter,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  )
  {
    bool isTriangle = p2.IsEqualTo(p3);

    List<double> rawVertices = isTriangle
      ? [p0.X, p0.Y, p0.Z, p1.X, p1.Y, p1.Z, p2.X, p2.Y, p2.Z]
      : [p0.X, p0.Y, p0.Z, p1.X, p1.Y, p1.Z, p2.X, p2.Y, p2.Z, p3.X, p3.Y, p3.Z];

    return new SOG.Mesh
    {
      vertices = referencePointConverter.ConvertWCSDoublesToExternalCoordinates(rawVertices),
      faces = isTriangle ? [3, 0, 1, 2] : [4, 0, 1, 2, 3],
      units = settingsStore.Current.SpeckleUnits,
    };
  }
}
