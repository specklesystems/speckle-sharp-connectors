using Bentley.GeometryNET;
using Speckle.Converter.MicroStation.Settings;
using Speckle.Converters.Common;
using Speckle.Objects.Geometry;

namespace Speckle.Converter.MicroStation.ToSpeckle;

/// <summary>
/// Last-resort converter for any managed DGN element that doesn't have a dedicated converter
/// (proprietary <c>ApplicationElement</c>, terrain models, civil features without a CifNET
/// surface, etc.) or whose dedicated converter throws. Produces an axis-aligned bounding-box
/// <see cref="Mesh"/> from the element's <see cref="DRange3d"/> via
/// <c>DisplayableElement.CalcElementRange</c>.
/// <para>
/// Real geometry extraction (terrain TINs, B-rep tessellation, etc.) requires deeper managed-API
/// work — solid primitive queries, polyface construction, terrain DTM iteration. This fallback
/// keeps Send stable for any element type while those land incrementally.
/// </para>
/// </summary>
public class FallbackElementMeshConverter(IConverterSettingsStore<MicroStationConversionSettings> settingsStore)
{
  public Mesh Convert(MgdElement element)
  {
    var s = settingsStore.Current;
    var applicationId = ((ulong)element.ElementId).ToString();

    DRange3d range = default;
    if (element is Bentley.DgnPlatformNET.Elements.DisplayableElement displayable)
    {
      displayable.CalcElementRange(out range);
    }

    return BuildBoundingBoxMesh(range.Low, range.High, s.SpeckleUnits, applicationId);
  }

  private static Mesh BuildBoundingBoxMesh(DPoint3d lo, DPoint3d hi, string units, string applicationId)
  {
    var verts = new List<double>
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

    // Six quad faces in n-gon encoding [n, i0, i1, ..., in-1].
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
      vertices = verts,
      faces = faces,
      units = units,
      applicationId = applicationId,
    };
  }
}
