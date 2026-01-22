using Autodesk.Revit.DB;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Services;
using Speckle.DoubleNumerics;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.RevitShared.ToHost.Raw.Geometry;

/// <summary>
/// Converts Speckle Mesh to Revit Solid for FreeFormElement creation in family documents.
/// </summary>
/// <remarks>
/// Why this exists alongside MeshConverterToHost:
/// - FreeFormElement.Create() requires DB.Solid, not DB.Mesh
/// - Different TessellatedShapeBuilder config: Target.Solid, Fallback.Abort, OpenConnectedFaceSet(true)
/// - No reference point transform applied - family geometry uses local coordinates
/// </remarks>
public class MeshToSolidConverter : ITypedConverter<SOG.Mesh, Solid?>
{
  private readonly ScalingServiceToHost _scalingService;

  public MeshToSolidConverter(ScalingServiceToHost scalingService)
  {
    _scalingService = scalingService;
  }

  public Solid? Convert(SOG.Mesh mesh)
  {
    using var tsb = new TessellatedShapeBuilder();
    tsb.Target = TessellatedShapeBuilderTarget.Solid;
    tsb.Fallback = TessellatedShapeBuilderFallback.Abort;
    tsb.GraphicsStyleId = ElementId.InvalidElementId;

    tsb.OpenConnectedFaceSet(true);

    var vertices = ArrayToPoints(mesh.vertices, mesh.units);

    int i = 0;
    while (i < mesh.faces.Count)
    {
      int n = mesh.faces[i];
      if (n < 3)
      {
        n += 3;
      }

      var points = mesh.faces.GetRange(i + 1, n).Select(x => vertices[x]).ToArray();

      if (IsNonPlanarQuad(points))
      {
        tsb.AddFace(new TessellatedFace(new List<XYZ> { points[0], points[1], points[3] }, ElementId.InvalidElementId));
        tsb.AddFace(new TessellatedFace(new List<XYZ> { points[1], points[2], points[3] }, ElementId.InvalidElementId));
      }
      else
      {
        tsb.AddFace(new TessellatedFace(points, ElementId.InvalidElementId));
      }

      i += n + 1;
    }

    tsb.CloseConnectedFaceSet();
    tsb.Build();

    return tsb.GetBuildResult().GetGeometricalObjects().OfType<Solid>().FirstOrDefault();
  }

  private XYZ[] ArrayToPoints(IList<double> arr, string units)
  {
    if (arr.Count % 3 != 0)
    {
      throw new ValidationException("Array malformed: length%3 != 0.");
    }

    var points = new XYZ[arr.Count / 3];
    var unitTypeId = _scalingService.UnitsToNative(units);

    for (int i = 2, k = 0; i < arr.Count; i += 3)
    {
      var x = _scalingService.ScaleToNative(arr[i - 2], unitTypeId);
      var y = _scalingService.ScaleToNative(arr[i - 1], unitTypeId);
      var z = _scalingService.ScaleToNative(arr[i], unitTypeId);

      points[k++] = new XYZ(x, y, z);
    }

    return points;
  }

  private static bool IsNonPlanarQuad(IList<XYZ> points)
  {
    if (points.Count != 4)
    {
      return false;
    }

    var matrix = new Matrix4x4(
      (float)points[0].X,
      (float)points[1].X,
      (float)points[2].X,
      (float)points[3].X,
      (float)points[0].Y,
      (float)points[1].Y,
      (float)points[2].Y,
      (float)points[3].Y,
      (float)points[0].Z,
      (float)points[1].Z,
      (float)points[2].Z,
      (float)points[3].Z,
      1,
      1,
      1,
      1
    );
    return matrix.GetDeterminant() != 0;
  }
}
