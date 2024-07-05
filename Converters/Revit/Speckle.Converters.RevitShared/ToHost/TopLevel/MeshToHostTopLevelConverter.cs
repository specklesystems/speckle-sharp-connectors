using System.DoubleNumerics;
using Autodesk.Revit.DB;
using Objects.Other;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.ToSpeckle;

namespace Speckle.Converters.RevitShared.ToHost.TopLevel;

[NameAndRankValue(nameof(SOG.Mesh), 0)]
public class MeshToHostTopLevelConverter
  : BaseTopLevelConverterToHost<SOG.Mesh, DB.GeometryObject[]>,
    ITypedConverter<SOG.Mesh, DB.GeometryObject[]>
{
  private readonly ITypedConverter<SOG.Point, DB.XYZ> _pointConverter;
  private readonly ITypedConverter<RenderMaterial, DB.Material> _materialConverter;

  public MeshToHostTopLevelConverter(
    ITypedConverter<SOG.Point, XYZ> pointConverter,
    ITypedConverter<RenderMaterial, DB.Material> materialConverter
  )
  {
    _pointConverter = pointConverter;
    _materialConverter = materialConverter;
  }

  public override GeometryObject[] Convert(SOG.Mesh mesh)
  {
    TessellatedShapeBuilderTarget target = TessellatedShapeBuilderTarget.Mesh;
    TessellatedShapeBuilderFallback fallback = TessellatedShapeBuilderFallback.Salvage;

    var tsb = new TessellatedShapeBuilder()
    {
      Fallback = fallback,
      Target = target,
      GraphicsStyleId = ElementId.InvalidElementId
    };

    var valid = tsb.AreTargetAndFallbackCompatible(target, fallback);
    //tsb.OpenConnectedFaceSet(target == TessellatedShapeBuilderTarget.Solid);
    tsb.OpenConnectedFaceSet(false);
    var vertices = ArrayToPoints(mesh.vertices, mesh.units);

    ElementId materialId = ElementId.InvalidElementId;
    if (mesh["renderMaterial"] is RenderMaterial renderMaterial)
    {
      materialId = _materialConverter.Convert(renderMaterial).Id;
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
        ;
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

    return result.GetGeometricalObjects().ToArray();
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
