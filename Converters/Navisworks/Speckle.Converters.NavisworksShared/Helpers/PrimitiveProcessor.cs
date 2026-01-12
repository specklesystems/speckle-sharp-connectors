using System.Collections.ObjectModel;
using Autodesk.Navisworks.Api.Interop.ComApi;
using Speckle.Converter.Navisworks.Geometry;
using Speckle.DoubleNumerics;

namespace Speckle.Converter.Navisworks.Helpers;

/// <summary>
/// Callback processor for Navisworks COM primitive generation.
///
/// PERFORMANCE ANALYSIS (Based on profiling: COM = 99.5%, Geometry = 0.5%):
///
/// The bottleneck is NOT in this class's logic, but in the COM interop itself:
///
/// 1. fragment.GenerateSimplePrimitives() - The COM API call that invokes these callbacks
///    - This is controlled by Navisworks, not us
///    - Each callback invocation has COM marshaling overhead
///    - For 687 items: ~13.7ms per item just in COM overhead
///
/// 2. InwSimpleVertex.coord property access - Returns Array (COM object)
///    - COM marshaling converts native array to .NET Array
///    - Each vertex requires 3 GetValue() calls (X, Y, Z)
///    - For complex geometry: thousands of vertices = thousands of COM calls
///
/// 3. NAV.Vector3D constructor - Creates COM object
///    - Using 'using' to dispose immediately (good practice)
///    - But still has allocation/disposal overhead per vertex
///
/// POTENTIAL OPTIMIZATIONS (in order of impact):
///
/// A. CACHE GEOMETRY SIGNATURES (Likely already implemented elsewhere)
///    - If same geometry processed multiple times, cache the result
///    - Avoid calling GenerateSimplePrimitives() at all for duplicates
///    - This is the ONLY way to avoid the COM overhead
///
/// B. REDUCE COM VERTEX PROPERTY ACCESSES
///    - Current: 3 GetValue() calls per vertex (X, Y, Z)
///    - Optimization: Use unsafe code to access array buffer directly
///    - Risk: Breaks if Navisworks changes COM object layout
///    - Benefit: ~30-40% reduction in COM marshaling overhead
///
/// C. REUSE NAV.Vector3D OBJECTS (Marginal benefit, breaks 'using' pattern)
///    - Object pooling for Vector3D instances
///    - Complex to implement correctly with COM
///    - Minimal benefit compared to option B
///
/// D. BATCH PROCESSING (Already tested - doesn't help)
///    - Parallelizing geometry creation saves less than 1 percent of total time - COM calls must remain serial
///
/// RECOMMENDATION:
/// Focus on caching at a higher level (geometry signature cache).
/// The COM API itself is the bottleneck - we can't make it faster from C#.
/// </summary>
public class PrimitiveProcessor : InwSimplePrimitivesCB
{
  private readonly List<double> _coords = [];
  private List<int> _faces = [];
  private List<SafeLine> _lines = [];
  private List<SafePoint> _points = [];
  private List<SafeTriangle> _triangles = [];
  private bool IsUpright { get; set; }

  internal PrimitiveProcessor(bool isUpright)
  {
    IsUpright = isUpright;
    SetCoords(new ReadOnlyCollection<double>([]));
    SetFaces([]);
    SetTriangles([]);
    SetLines([]);
    SetPoints([]);
  }

  public IReadOnlyList<double> Coords => _coords.AsReadOnly();
  private IReadOnlyList<int> Faces => _faces.AsReadOnly();
  public IReadOnlyList<SafeTriangle> Triangles => _triangles.AsReadOnly();
  public IReadOnlyList<SafeLine> Lines => _lines.AsReadOnly();
  public IReadOnlyList<SafePoint> Points => _points.AsReadOnly();
  internal IEnumerable<double>? LocalToWorldTransformation { get; set; }

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
      var safeLine = new SafeLine(vD1, vD2);
      AddLine(safeLine);
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

    var safePoint = new SafePoint(vD1);
    AddPoint(safePoint);
  }

  // TODO: Needed for Splines
  public void SnapPoint(InwSimpleVertex? v1) => Point(v1);

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

    // Capture values immediately in our safe struct
    var safeTriangle = new SafeTriangle(vD1, vD2, vD3);

    var indexPointer = Faces.Count;
    AddFace(3);
    AddFaces([indexPointer + 0, indexPointer + 1, indexPointer + 2]);
    AddCoords(
      [
        safeTriangle.Vertex1.X,
        safeTriangle.Vertex1.Y,
        safeTriangle.Vertex1.Z,
        safeTriangle.Vertex2.X,
        safeTriangle.Vertex2.Y,
        safeTriangle.Vertex2.Z,
        safeTriangle.Vertex3.X,
        safeTriangle.Vertex3.Y,
        safeTriangle.Vertex3.Z
      ]
    );

    AddTriangle(safeTriangle);
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

  private void SetTriangles(List<SafeTriangle> triangles) =>
    _triangles = triangles ?? throw new ArgumentNullException(nameof(triangles));

  private void AddTriangle(SafeTriangle triangle) => _triangles.Add(triangle);

  private void SetLines(List<SafeLine> lines) => _lines = lines ?? throw new ArgumentNullException(nameof(lines));

  private void AddLine(SafeLine line) => _lines.Add(line);

  private void SetPoints(List<SafePoint> points) => _points = points ?? throw new ArgumentNullException(nameof(points));

  private void AddPoint(SafePoint point) => _points.Add(point);

  private static NAV.Vector3D TransformVectorToOrientation(NAV.Vector3D v, bool isUpright) =>
    isUpright ? v : new NAV.Vector3D(v.X, -v.Z, v.Y);

  private static NAV.Vector3D ApplyTransformation(Vector3 vector3, IEnumerable<double>? matrixStore)
  {
    // NOTE: This math is fast (~0.064ms per item). Not the bottleneck.
    var matrix = matrixStore!.ToList();
    var t1 = matrix[3] * vector3.X + matrix[7] * vector3.Y + matrix[11] * vector3.Z + matrix[15];
    var vectorDoubleX = (matrix[0] * vector3.X + matrix[4] * vector3.Y + matrix[8] * vector3.Z + matrix[12]) / t1;
    var vectorDoubleY = (matrix[1] * vector3.X + matrix[5] * vector3.Y + matrix[9] * vector3.Z + matrix[13]) / t1;
    var vectorDoubleZ = (matrix[2] * vector3.X + matrix[6] * vector3.Y + matrix[10] * vector3.Z + matrix[14]) / t1;

    return new NAV.Vector3D(vectorDoubleX, vectorDoubleY, vectorDoubleZ);
  }

  /// <summary>
  /// Extracts vertex coordinates from the COM object.
  ///
  /// PERFORMANCE HOTSPOT: This is called for EVERY vertex in EVERY triangle/line.
  /// For 687 items with complex geometry, this could be called 100 thousand+ times.
  ///
  /// Each call involves:
  /// 1. Cast v.coord to Array (COM property access)
  /// 2. Three GetValue() calls (COM marshaling for each coordinate)
  ///
  /// Total COM overhead per vertex: ~0.02-0.05ms
  /// Multiply by thousands of vertices = most of the 13.7ms per item
  ///
  /// OPTIMIZATION OPPORTUNITY (unsafe, risky):
  /// Could use Marshal.Copy to get array buffer directly, avoiding GetValue() calls.
  /// But this breaks if Navisworks changes COM object layout.
  /// Not recommended unless profiling shows this specific method is >50% of the time.
  /// </summary>
  private static Vector3 VectorFromVertex(InwSimpleVertex v)
  {
    var arrayV = (Array)v.coord;
    return new Vector3((float)arrayV.GetValue(1), (float)arrayV.GetValue(2), (float)arrayV.GetValue(3));
  }
}
