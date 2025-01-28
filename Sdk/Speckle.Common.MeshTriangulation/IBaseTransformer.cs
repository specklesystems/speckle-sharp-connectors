using Speckle.DoubleNumerics;


namespace Speckle.Common.MeshTriangulation;

public interface IBaseTransformer
{
  void SetTargetPlane(Poly3 plane);
  Vector2 Vec3ToVec2(Vector3 vec3);
  Vector3 Vec2ToVec3(Vector2 vec2);
  Poly2 Poly3ToPoly2(Poly3 poly3);
  Poly3 Poly2ToPoly3(Poly2 poly2);
  Mesh2 Mesh3ToMesh2(Mesh3 mesh3);
  Mesh3 Mesh2ToMesh3(Mesh2 mesh2);
}
