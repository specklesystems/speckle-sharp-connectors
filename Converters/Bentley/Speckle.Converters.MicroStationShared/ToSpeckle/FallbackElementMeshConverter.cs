using Speckle.Converter.MicroStation.Settings;
using Speckle.Converters.Common;
using Speckle.Objects.Geometry;

namespace Speckle.Converter.MicroStation.ToSpeckle;

/// <summary>
/// Last-resort converter for any DGN element type that does not have a dedicated
/// <see cref="IToSpeckleTopLevelConverter"/> registered (terrain models, civil features,
/// constraint elements — generic "Type 106" extended elements not surfaced by name in the
/// 2026 COM <c>MsdElementType</c> enum).
/// <para>
/// Produces an axis-aligned bounding-box <see cref="Mesh"/> from <see cref="MSIDGN.Element.Range"/>.
/// Real geometry extraction (terrain TINs, civil corridors, B-rep tessellation) lives in
/// <c>Bentley.DgnPlatformNET</c> / <c>Bentley.GeometryNET</c> / <c>Bentley.TerrainModelNET</c>,
/// but pulling those mixed-mode C++/CLI assemblies into the conversion path triggered process-
/// terminating crashes during Send (likely Corrupted State Exceptions from native COM interop
/// that bypass normal try/catch). Sticking to the bounding box for now keeps Send stable;
/// per-type specialised converters can be reintroduced once we have a robust native-interop
/// strategy that won't tear down MicroStation on errors.
/// </para>
/// </summary>
public class FallbackElementMeshConverter(IConverterSettingsStore<MicroStationConversionSettings> settingsStore)
{
  public Mesh Convert(Element comElement)
  {
    var s = settingsStore.Current;
    return CreateBoundingBoxMesh(comElement, s.SpeckleUnits);
  }

  private static Mesh CreateBoundingBoxMesh(Element element, string units)
  {
    var range = element.Range;
    var lo = range.Low;
    var hi = range.High;

    var vertices = new List<double>
    {
      lo.X, lo.Y, lo.Z,
      hi.X, lo.Y, lo.Z,
      hi.X, hi.Y, lo.Z,
      lo.X, hi.Y, lo.Z,
      lo.X, lo.Y, hi.Z,
      hi.X, lo.Y, hi.Z,
      hi.X, hi.Y, hi.Z,
      lo.X, hi.Y, hi.Z,
    };

    // Six quad faces (bottom, top, front, back, left, right) in n-gon encoding [n, i0, i1, ..., in-1]
    var faces = new List<int>
    {
      4, 0, 1, 2, 3,
      4, 4, 5, 6, 7,
      4, 0, 1, 5, 4,
      4, 2, 3, 7, 6,
      4, 1, 2, 6, 5,
      4, 0, 3, 7, 4,
    };

    return new Mesh
    {
      vertices = vertices,
      faces = faces,
      units = units,
      applicationId = element.ID.ToString(),
    };
  }
}
