using Speckle.Objects.Geometry;

namespace Speckle.Converter.Navisworks.Helpers;

public readonly record struct Aabb(double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ)
{
  public bool IsValid => !(MinX == 0 && MinY == 0 && MinZ == 0 && MaxX == 0 && MaxY == 0 && MaxZ == 0);
}

public static class GeometryHelpers
{
  /// <summary>
  /// Compares two vectors to determine if they are approximately equal within a given tolerance.
  /// </summary>
  /// <param name="vectorA">The first comparison vector.</param>
  /// <param name="vectorB">The second comparison vector.</param>
  /// <param name="tolerance">The tolerance value for the comparison. The default is 1e-9.</param>
  /// <returns>True if the vectors match within the tolerance; otherwise, false.</returns>
  internal static bool VectorMatch(NAV.Vector3D vectorA, NAV.Vector3D vectorB, double tolerance = 1e-9) =>
    Math.Abs(vectorA.X - vectorB.X) < tolerance
    && Math.Abs(vectorA.Y - vectorB.Y) < tolerance
    && Math.Abs(vectorA.Z - vectorB.Z) < tolerance;

  internal static double[] InvertRigid(double[] m)
  {
    // Rigid: [ R 0; t 1 ] in row major
    // inv = [ R^T 0; -t*R^T 1 ]
    var inv = new double[16];

    // transpose 3x3 rotation
    inv[0] = m[0];
    inv[1] = m[4];
    inv[2] = m[8];
    inv[3] = 0;
    inv[4] = m[1];
    inv[5] = m[5];
    inv[6] = m[9];
    inv[7] = 0;
    inv[8] = m[2];
    inv[9] = m[6];
    inv[10] = m[10];
    inv[11] = 0;

    var tx = m[12];
    var ty = m[13];
    var tz = m[14];

    // -t * R^T
    inv[12] = -(tx * inv[0] + ty * inv[4] + tz * inv[8]);
    inv[13] = -(tx * inv[1] + ty * inv[5] + tz * inv[9]);
    inv[14] = -(tx * inv[2] + ty * inv[6] + tz * inv[10]);
    inv[15] = 1;

    return inv;
  }

  private static void TransformPointInPlace(double[] m, ref double x, ref double y, ref double z)
  {
    var nx = x * m[0] + y * m[4] + z * m[8] + m[12];
    var ny = x * m[1] + y * m[5] + z * m[9] + m[13];
    var nz = x * m[2] + y * m[6] + z * m[10] + m[14];
    x = nx;
    y = ny;
    z = nz;
  }

  // ReSharper disable once UnusedMember.Local
  private static void UnbakeMeshVertices(Mesh mesh, double[] invWorld)
  {
    for (int i = 0; i < mesh.vertices.Count; i += 3)
    {
      double x = mesh.vertices[i];
      double y = mesh.vertices[i + 1];
      double z = mesh.vertices[i + 2];

      TransformPointInPlace(invWorld, ref x, ref y, ref z);

      mesh.vertices[i] = x;
      mesh.vertices[i + 1] = y;
      mesh.vertices[i + 2] = z;
    }
  }

  // ReSharper disable once UnusedMember.Local
  private static void UnbakeLine(Line line, double[] invWorld)
  {
    double sx = line.start.x,
      sy = line.start.y,
      sz = line.start.z;
    double ex = line.end.x,
      ey = line.end.y,
      ez = line.end.z;

    TransformPointInPlace(invWorld, ref sx, ref sy, ref sz);
    TransformPointInPlace(invWorld, ref ex, ref ey, ref ez);

    line.start.x = sx;
    line.start.y = sy;
    line.start.z = sz;
    line.end.x = ex;
    line.end.y = ey;
    line.end.z = ez;
  }

  internal static Aabb Aabb(Mesh mesh)
  {
    double minX = double.PositiveInfinity,
      minY = double.PositiveInfinity,
      minZ = double.PositiveInfinity;
    double maxX = double.NegativeInfinity,
      maxY = double.NegativeInfinity,
      maxZ = double.NegativeInfinity;

    for (int i = 0; i < mesh.vertices.Count; i += 3)
    {
      var x = mesh.vertices[i];
      var y = mesh.vertices[i + 1];
      var z = mesh.vertices[i + 2];

      if (x < minX)
      {
        minX = x;
      }

      if (y < minY)
      {
        minY = y;
      }

      if (z < minZ)
      {
        minZ = z;
      }

      if (x > maxX)
      {
        maxX = x;
      }

      if (y > maxY)
      {
        maxY = y;
      }

      if (z > maxZ)
      {
        maxZ = z;
      }
    }

    return new Aabb(minX, minY, minZ, maxX, maxY, maxZ);
  }

  private static bool NearlyEqual(double a, double b, double eps) => Math.Abs(a - b) <= eps;

  private static bool AabbEqual(Aabb a, Aabb b, double eps) =>
    NearlyEqual(a.MinX, b.MinX, eps)
    && NearlyEqual(a.MinY, b.MinY, eps)
    && NearlyEqual(a.MinZ, b.MinZ, eps)
    && NearlyEqual(a.MaxX, b.MaxX, eps)
    && NearlyEqual(a.MaxY, b.MaxY, eps)
    && NearlyEqual(a.MaxZ, b.MaxZ, eps);

  internal static Aabb ComputeUnbakedAabb(PrimitiveProcessor processor, double[] invWorld)
  {
    var hasAny = false;

    double minX = double.PositiveInfinity,
      minY = double.PositiveInfinity,
      minZ = double.PositiveInfinity;
    double maxX = double.NegativeInfinity,
      maxY = double.NegativeInfinity,
      maxZ = double.NegativeInfinity;

    void AddPoint(double x, double y, double z)
    {
      TransformPointInPlace(invWorld, ref x, ref y, ref z);

      hasAny = true;
      if (x < minX)
      {
        minX = x;
      }

      if (y < minY)
      {
        minY = y;
      }

      if (z < minZ)
      {
        minZ = z;
      }

      if (x > maxX)
      {
        maxX = x;
      }

      if (y > maxY)
      {
        maxY = y;
      }

      if (z > maxZ)
      {
        maxZ = z;
      }
    }

    foreach (var t in processor.Triangles)
    {
      AddPoint(t.Vertex1.X, t.Vertex1.Y, t.Vertex1.Z);
      AddPoint(t.Vertex2.X, t.Vertex2.Y, t.Vertex2.Z);
      AddPoint(t.Vertex3.X, t.Vertex3.Y, t.Vertex3.Z);
    }

    foreach (var l in processor.Lines)
    {
      AddPoint(l.Start.X, l.Start.Y, l.Start.Z);
      AddPoint(l.End.X, l.End.Y, l.End.Z);
    }

    return hasAny ? new Aabb(minX, minY, minZ, maxX, maxY, maxZ) : default;
  }

  private static void Acc(
    double[] m,
    double x,
    double y,
    double z,
    ref double minX,
    ref double minY,
    ref double minZ,
    ref double maxX,
    ref double maxY,
    ref double maxZ
  )
  {
    // apply transform (row major with translation at 12,13,14 as per your usage)
    var nx = x * m[0] + y * m[4] + z * m[8] + m[12];
    var ny = x * m[1] + y * m[5] + z * m[9] + m[13];
    var nz = x * m[2] + y * m[6] + z * m[10] + m[14];

    if (nx < minX)
    {
      minX = nx;
    }

    if (ny < minY)
    {
      minY = ny;
    }

    if (nz < minZ)
    {
      minZ = nz;
    }

    if (nx > maxX)
    {
      maxX = nx;
    }

    if (ny > maxY)
    {
      maxY = ny;
    }

    if (nz > maxZ)
    {
      maxZ = nz;
    }
  }

  internal static bool NearlyEqual(Aabb a, Aabb b, double eps) =>
    Math.Abs(a.MinX - b.MinX) <= eps
    && Math.Abs(a.MinY - b.MinY) <= eps
    && Math.Abs(a.MinZ - b.MinZ) <= eps
    && Math.Abs(a.MaxX - b.MaxX) <= eps
    && Math.Abs(a.MaxY - b.MaxY) <= eps
    && Math.Abs(a.MaxZ - b.MaxZ) <= eps;
}
