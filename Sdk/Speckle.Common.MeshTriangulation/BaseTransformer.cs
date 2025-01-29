using Speckle.DoubleNumerics;

namespace Speckle.Common.MeshTriangulation;

public class BaseTransformer : IBaseTransformer
{
  private Matrix4x4 _2DTo3DMatrix;
  private Matrix4x4 _3DTo2DMatrix;
  private double _zOffset;

  // creates a target coordinate system, where the Z axis is the plane normal
  public void SetTargetPlane(Poly3 plane)
  {
    Vector3 normal = plane.GetNormal();
    var (xBasis, yBasis) = ComputeBasisVectors(normal);

    _2DTo3DMatrix = GetTransformationMatrix(xBasis, yBasis, normal);
    Matrix4x4.Invert(_2DTo3DMatrix, out _3DTo2DMatrix);

    var transformedVertex = Vector3.Transform(plane.Vertices[0], _3DTo2DMatrix);
    _zOffset = transformedVertex.Z;
  }

  public Vector3 Vec2ToVec3(Vector2 vec2)
  {
    return Vector3.Transform(new Vector3(vec2.X, vec2.Y, _zOffset), _2DTo3DMatrix);
  }

  public Vector2 Vec3ToVec2(Vector3 vec3)
  {
    var transformedVertex = Vector3.Transform(vec3, _3DTo2DMatrix);
    return new Vector2(transformedVertex.X, transformedVertex.Y);
  }

  public Poly3 Poly2ToPoly3(Poly2 poly2)
  {
    var poly3 = new Poly3();

    foreach (var vertex in poly2.Vertices)
    {
      poly3.Vertices.Add(Vec2ToVec3(vertex));
    }

    return poly3;
  }

  public Poly2 Poly3ToPoly2(Poly3 poly3)
  {
    var poly2 = new Poly2();

    foreach (var vertex in poly3.Vertices)
    {
      poly2.Vertices.Add(Vec3ToVec2(vertex));
    }

    return poly2;
  }

  public Mesh3 Mesh2ToMesh3(Mesh2 mesh2)
  {
    var vertices = new List<Vector3>();
    foreach (var vertex in mesh2.Vertices)
    {
      vertices.Add(Vec2ToVec3(vertex));
    }

    return new Mesh3(vertices, new List<int>(mesh2.Triangles));
  }

  public Mesh2 Mesh3ToMesh2(Mesh3 mesh3)
  {
    var vertices = new List<Vector2>();
    foreach (var vertex in mesh3.Vertices)
    {
      vertices.Add(Vec3ToVec2(vertex));
    }

    return new Mesh2(vertices, new List<int>(mesh3.Triangles));
  }

  private (Vector3 xBasis, Vector3 yBasis) ComputeBasisVectors(Vector3 normal)
  {
    // We need two arbitrary orthogonal vectors in the polygon plane
    // Find an arbitrary vector not parallel to the normal
    Vector3 arbitrary = Vector3.UnitZ;
    var angle = Vector3.Dot(normal, arbitrary);
    if (Math.Abs(angle) > 0.99f)
    {
      arbitrary = Vector3.UnitY;
    }

    // Compute the X and Y basis vectors
    Vector3 xBasis = Vector3.Normalize(Vector3.Cross(normal, arbitrary));
    Vector3 yBasis = Vector3.Normalize(Vector3.Cross(normal, xBasis));

    return (xBasis, yBasis);
  }

  private Matrix4x4 GetTransformationMatrix(Vector3 xBasis, Vector3 yBasis, Vector3 normal)
  {
    return new Matrix4x4(
        xBasis.X, xBasis.Y, xBasis.Z, 0,
        yBasis.X, yBasis.Y, yBasis.Z, 0,
        normal.X, normal.Y, normal.Z, 0,
        0, 0, 0, 1);
  }
}
