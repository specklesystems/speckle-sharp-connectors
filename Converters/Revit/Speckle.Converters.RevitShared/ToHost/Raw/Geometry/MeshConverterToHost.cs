using Autodesk.Revit.DB;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.DoubleNumerics;

namespace Speckle.Converters.RevitShared.ToHost.TopLevel;

public class MeshConverterToHost : ITypedConverter<SOG.Mesh, List<DB.GeometryObject>>
{
  private readonly ITypedConverter<SOG.Point, DB.XYZ> _pointConverter;
  private readonly RevitToHostCacheSingleton _revitToHostCacheSingleton;

  public MeshConverterToHost(
    ITypedConverter<SOG.Point, XYZ> pointConverter,
    RevitToHostCacheSingleton revitToHostCacheSingleton
  )
  {
    _pointConverter = pointConverter;
    _revitToHostCacheSingleton = revitToHostCacheSingleton;
  }

  public List<DB.GeometryObject> Convert(SOG.Mesh mesh)
  {
    TessellatedShapeBuilderTarget target = TessellatedShapeBuilderTarget.Mesh;
    TessellatedShapeBuilderFallback fallback = TessellatedShapeBuilderFallback.Salvage;

    using var tsb = new TessellatedShapeBuilder()
    {
      Fallback = fallback,
      Target = target,
      GraphicsStyleId = ElementId.InvalidElementId
    };

    var valid = tsb.AreTargetAndFallbackCompatible(target, fallback);
    tsb.OpenConnectedFaceSet(false);
    var vertices = ArrayToPoints(mesh.vertices, mesh.units);

    ElementId materialId = ElementId.InvalidElementId;

    if (
      _revitToHostCacheSingleton.MaterialsByObjectId.TryGetValue(mesh.applicationId ?? mesh.id, out var mappedElementId)
    )
    {
      materialId = mappedElementId;
    }

    int i = 0;
    while (i < mesh.faces.Count)
    {
      int n = mesh.faces[i];
      if (n < 3)
      {
        n += 3; // 0 -> 3, 1 -> 4 to preserve backwards compatibility
      }

      var points = mesh.faces.GetRange(i + 1, n).Select(x => vertices[x]).ToArray();

      if (IsNonPlanarQuad(points))
      {
        //Non-planar quads will be triangulated as it's more desirable than `TessellatedShapeBuilder.Build`'s attempt to make them planar.
        //TODO consider triangulating all n > 3 polygons that are non-planar
        var triPoints = new List<XYZ> { points[0], points[1], points[3] };
        var face1 = new TessellatedFace(triPoints, materialId);
        tsb.AddFace(face1);

        triPoints = new List<XYZ> { points[1], points[2], points[3] };

        var face2 = new TessellatedFace(triPoints, materialId);
        tsb.AddFace(face2);
      }
      else
      {
        var face = new TessellatedFace(points, materialId);
        tsb.AddFace(face);
      }

      i += n + 1;
    }

    tsb.CloseConnectedFaceSet();

    tsb.Build();
    var result = tsb.GetBuildResult();

    return result.GetGeometricalObjects().ToList();
  }

  private static bool IsNonPlanarQuad(IList<XYZ> points)
  {
    if (points.Count != 4)
    {
      return false;
    }

    var matrix = new Matrix4x4(
      points[0].X,
      points[1].X,
      points[2].X,
      points[3].X,
      points[0].Y,
      points[1].Y,
      points[2].Y,
      points[3].Y,
      points[0].Z,
      points[1].Z,
      points[2].Z,
      points[3].Z,
      1,
      1,
      1,
      1
    );
    return matrix.GetDeterminant() != 0;
  }

  private XYZ[] ArrayToPoints(IList<double> arr, string units)
  {
    if (arr.Count % 3 != 0)
    {
      throw new SpeckleConversionException("Array malformed: length%3 != 0.");
    }

    XYZ[] points = new XYZ[arr.Count / 3];

    for (int i = 2, k = 0; i < arr.Count; i += 3)
    {
      var point = new SOG.Point(arr[i - 2], arr[i - 1], arr[i], units);
      points[k++] = _pointConverter.Convert(point);
    }

    return points;
  }
}
