using System.Collections.ObjectModel;
using Autodesk.Navisworks.Api.Interop.ComApi;
using Speckle.Converter.Navisworks.Geometry;
using Speckle.DoubleNumerics;

namespace Speckle.Converter.Navisworks.Helpers;

public class PrimitiveProcessor : InwSimplePrimitivesCB
{
  private readonly List<double> _coords = [];
  private List<int> _faces = [];
  private List<LineD> _lines = [];
  private List<PointD> _points = [];
  private List<TriangleD> _triangles = [];
  private bool IsUpright { get; set; }

  public PrimitiveProcessor(bool isUpright, IEnumerable<double> localToWorldTransformation)
    : this(localToWorldTransformation)
  {
    IsUpright = isUpright;
  }

  private PrimitiveProcessor(IEnumerable<double> localToWorldTransformation)
  {
    LocalToWorldTransformation = localToWorldTransformation;
    SetCoords(new ReadOnlyCollection<double>([]));
    SetFaces([]);
    SetTriangles([]);
    SetLines([]);
    SetPoints([]);
  }

  public IReadOnlyList<double> Coords => _coords.AsReadOnly();
  private IReadOnlyList<int> Faces => _faces.AsReadOnly();
  public IReadOnlyList<TriangleD> Triangles => _triangles.AsReadOnly();
  public IReadOnlyList<LineD> Lines => _lines.AsReadOnly();
  public IReadOnlyList<PointD> Points => _points.AsReadOnly();
  private IEnumerable<double> LocalToWorldTransformation { get; set; }

  public void Line(InwSimpleVertex? v1, InwSimpleVertex? v2)
  {
    if (v1 == null || v2 == null)
    {
      return;
    }

    using var vD1 = TransformVectorToOrientation(
      ApplyTransformation(VectorFromVertex(v1), LocalToWorldTransformation),
      IsUpright
    );
    using var vD2 = TransformVectorToOrientation(
      ApplyTransformation(VectorFromVertex(v2), LocalToWorldTransformation),
      IsUpright
    );

    try
    {
      var line = new LineD(vD1, vD2);
      AddLine(line);
    }
    catch (ArgumentException ex)
    {
      Console.WriteLine($"ArgumentException caught: {ex.Message}");
    }
    catch (InvalidOperationException ex)
    {
      Console.WriteLine($"InvalidOperationException caught: {ex.Message}");
    }
  }

  public void Point(InwSimpleVertex? v1)
  {
    if (v1 == null)
    {
      return;
    }

    using var vD1 = TransformVectorToOrientation(
      ApplyTransformation(VectorFromVertex(v1), LocalToWorldTransformation),
      IsUpright
    );

    AddPoint(new PointD(vD1));
  }

  // TODO: Needed for Splines
  public void SnapPoint(InwSimpleVertex? v1) => throw new NotImplementedException();

  public void Triangle(InwSimpleVertex? v1, InwSimpleVertex? v2, InwSimpleVertex? v3)
  {
    if (v1 == null || v2 == null || v3 == null)
    {
      return;
    }

    using var vD1 = TransformVectorToOrientation(
      ApplyTransformation(VectorFromVertex(v1), LocalToWorldTransformation),
      IsUpright
    );
    using var vD2 = TransformVectorToOrientation(
      ApplyTransformation(VectorFromVertex(v2), LocalToWorldTransformation),
      IsUpright
    );
    using var vD3 = TransformVectorToOrientation(
      ApplyTransformation(VectorFromVertex(v3), LocalToWorldTransformation),
      IsUpright
    );

    var indexPointer = Faces.Count;
    AddFace(3);
    AddFaces([indexPointer + 0, indexPointer + 1, indexPointer + 2]);
    AddCoords([vD1.X, vD1.Y, vD1.Z, vD2.X, vD2.Y, vD2.Z, vD3.X, vD3.Y, vD3.Z]);
    AddTriangle(new TriangleD(vD1, vD2, vD3));
  }

  private void SetCoords(IEnumerable<double> coords)
  {
    _coords.Clear();
    _coords.AddRange(coords);
  }

  private void AddCoords(IEnumerable<double> coords) => _coords.AddRange(coords);

  private void SetFaces(List<int> faces) => _faces = faces ?? throw new ArgumentNullException(nameof(faces));

  private void AddFace(int face) => _faces.Add(face);

  private void AddFaces(IEnumerable<int> faces) => _faces.AddRange(faces);

  private void SetTriangles(List<TriangleD> triangles) =>
    _triangles = triangles ?? throw new ArgumentNullException(nameof(triangles));

  private void AddTriangle(TriangleD triangle) => _triangles.Add(triangle);

  private void SetLines(List<LineD> lines) => _lines = lines ?? throw new ArgumentNullException(nameof(lines));

  private void AddLine(LineD line) => _lines.Add(line);

  private void SetPoints(List<PointD> points) => _points = points ?? throw new ArgumentNullException(nameof(points));

  private void AddPoint(PointD point) => _points.Add(point);

  private static NAV.Vector3D TransformVectorToOrientation(NAV.Vector3D v, bool isUpright) =>
    isUpright ? v : new NAV.Vector3D(v.X, -v.Z, v.Y);

  private static NAV.Vector3D ApplyTransformation(Vector3 vector3, IEnumerable<double> matrixStore)
  {
    var matrix = matrixStore.ToList();
    var t1 = matrix[3] * vector3.X + matrix[7] * vector3.Y + matrix[11] * vector3.Z + matrix[15];
    var vectorDoubleX = (matrix[0] * vector3.X + matrix[4] * vector3.Y + matrix[8] * vector3.Z + matrix[12]) / t1;
    var vectorDoubleY = (matrix[1] * vector3.X + matrix[5] * vector3.Y + matrix[9] * vector3.Z + matrix[13]) / t1;
    var vectorDoubleZ = (matrix[2] * vector3.X + matrix[6] * vector3.Y + matrix[10] * vector3.Z + matrix[14]) / t1;

    return new NAV.Vector3D(vectorDoubleX, vectorDoubleY, vectorDoubleZ);
  }

  private static Vector3 VectorFromVertex(InwSimpleVertex v)
  {
    var arrayV = (Array)v.coord;
    return new Vector3((float)arrayV.GetValue(1), (float)arrayV.GetValue(2), (float)arrayV.GetValue(3));
  }
}
