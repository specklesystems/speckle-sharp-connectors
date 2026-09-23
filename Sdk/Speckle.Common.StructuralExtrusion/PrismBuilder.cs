using Speckle.Common.MeshTriangulation;
using Speckle.DoubleNumerics;

namespace Speckle.Common.StructuralExtrusion;

/// <summary>
/// Builds capped prisms from planar outlines. Side quads come from index arithmetic; only the two caps go through
/// the tessellator, and always in the outline's local frame (ENG-9048, ADR-0003).
/// </summary>
public static class PrismBuilder
{
  private const double NORMAL_EPSILON = 1e-12;
  private const double VERTICAL_REFERENCE_LIMIT = 0.9;

  public static PrismTemplate? TryBuildUnitPrism(ProfileOutline outline)
  {
    if (outline is null)
    {
      throw new ArgumentNullException(nameof(outline));
    }

    var built = TryBuildLocal(outline.Outer, outline.Holes, 0, 1);
    return built is null ? null : new PrismTemplate(built.Value.Vertices, built.Value.Faces);
  }

  public static PrismMesh Place(PrismTemplate template, Vector3 origin, LocalFrame frame, double length) =>
    Place(template, origin, frame, length, Vector2.Zero, Vector2.Zero);

  /// <summary>
  /// Places the prism with the profile shifted in its own plane (x depth, y width) by <paramref name="startOffset"/>
  /// at the origin end and <paramref name="endOffset"/> at the far end; unequal offsets give a sheared prism, which
  /// is still one affine map per cached vertex.
  /// </summary>
  public static PrismMesh Place(
    PrismTemplate template,
    Vector3 origin,
    LocalFrame frame,
    double length,
    Vector2 startOffset,
    Vector2 endOffset
  )
  {
    if (template is null)
    {
      throw new ArgumentNullException(nameof(template));
    }

    var local = template.LocalVertices;
    var world = new double[local.Count];
    for (int i = 0; i < local.Count; i += 3)
    {
      double z = local[i + 2];
      double x = local[i] + startOffset.X + z * (endOffset.X - startOffset.X);
      double y = local[i + 1] + startOffset.Y + z * (endOffset.Y - startOffset.Y);
      var p = frame.ToWorld(origin, x, y, z * length);
      world[i] = p.X;
      world[i + 1] = p.Y;
      world[i + 2] = p.Z;
    }
    return new PrismMesh(world, template.Faces);
  }

  /// <summary>Extrudes a planar 3D outline half the thickness to each side of its plane; null when degenerate.</summary>
  public static PrismMesh? TryExtrudeOutline(IReadOnlyList<Vector3> outline, double thickness)
  {
    if (outline is null)
    {
      throw new ArgumentNullException(nameof(outline));
    }
    if (outline.Count < 3 || double.IsNaN(thickness) || thickness <= 0)
    {
      return null;
    }

    var normal = PolygonMath.NewellNormal(outline);
    if (normal.LengthSquared() < NORMAL_EPSILON)
    {
      return null;
    }
    normal = Vector3.Normalize(normal);

    var reference = Math.Abs(normal.Z) < VERTICAL_REFERENCE_LIMIT ? Vector3.UnitZ : Vector3.UnitX;
    var frame = LocalFrame.TryCreate(normal, reference, 0);
    if (frame is null)
    {
      return null;
    }

    var centre = Vector3.Zero;
    foreach (var p in outline)
    {
      centre += p;
    }
    centre /= outline.Count;

    var local2D = new List<Vector2>(outline.Count);
    foreach (var p in outline)
    {
      var d = p - centre;
      local2D.Add(new Vector2(Vector3.Dot(d, frame.Value.XAxis), Vector3.Dot(d, frame.Value.YAxis)));
    }
    var outer = PolygonMath.WithWinding(local2D, counterClockwise: true);
    if (PolygonMath.SignedArea(outer) < PolygonMath.AREA_EPSILON)
    {
      return null;
    }

    var built = TryBuildLocal(outer, Array.Empty<IReadOnlyList<Vector2>>(), -thickness / 2, thickness / 2);
    if (built is null)
    {
      return null;
    }

    var local = built.Value.Vertices;
    var world = new double[local.Count];
    for (int i = 0; i < local.Count; i += 3)
    {
      var p = frame.Value.ToWorld(centre, local[i], local[i + 1], local[i + 2]);
      world[i] = p.X;
      world[i + 1] = p.Y;
      world[i + 2] = p.Z;
    }
    return new PrismMesh(world, built.Value.Faces);
  }

  // Contours must already be wound: outer counter-clockwise, holes clockwise, so every side quad faces the solid's
  // exterior (hole walls face into the void).
  private static (List<double> Vertices, List<int> Faces)? TryBuildLocal(
    IReadOnlyList<Vector2> outer,
    IReadOnlyList<IReadOnlyList<Vector2>> holes,
    double z0,
    double z1
  )
  {
    var vertices = new List<double>();
    var faces = new List<int>();
    var ringIndexByPosition = new Dictionary<(float X, float Y), (int Near, int Far)>();

    AddSides(outer, z0, z1, vertices, faces, ringIndexByPosition);
    foreach (var hole in holes)
    {
      AddSides(hole, z0, z1, vertices, faces, ringIndexByPosition);
    }

    var polygons = new List<Poly2>(1 + holes.Count) { new(new List<Vector2>(outer)) };
    foreach (var hole in holes)
    {
      polygons.Add(new Poly2(new List<Vector2>(hole)));
    }
    var cap = new LibTessTriangulator().Triangulate(polygons);
    if (cap.Triangles.Count == 0)
    {
      return null;
    }

    AddCap(cap, z1, counterClockwise: true, vertices, faces, ringIndexByPosition, far: true);
    AddCap(cap, z0, counterClockwise: false, vertices, faces, ringIndexByPosition, far: false);
    return (vertices, faces);
  }

  private static void AddSides(
    IReadOnlyList<Vector2> contour,
    double z0,
    double z1,
    List<double> vertices,
    List<int> faces,
    Dictionary<(float X, float Y), (int Near, int Far)> ringIndexByPosition
  )
  {
    int n = contour.Count;
    int baseIndex = vertices.Count / 3;
    foreach (var p in contour)
    {
      vertices.Add(p.X);
      vertices.Add(p.Y);
      vertices.Add(z0);
    }
    foreach (var p in contour)
    {
      vertices.Add(p.X);
      vertices.Add(p.Y);
      vertices.Add(z1);
    }
    for (int i = 0; i < n; i++)
    {
      ringIndexByPosition[((float)contour[i].X, (float)contour[i].Y)] = (baseIndex + i, baseIndex + n + i);
    }

    for (int i = 0; i < n; i++)
    {
      int j = (i + 1) % n;
      int a0 = baseIndex + i;
      int b0 = baseIndex + j;
      int a1 = baseIndex + n + i;
      int b1 = baseIndex + n + j;
      faces.Add(3);
      faces.Add(a0);
      faces.Add(b0);
      faces.Add(b1);
      faces.Add(3);
      faces.Add(a0);
      faces.Add(b1);
      faces.Add(a1);
    }
  }

  // The tessellator returns unindexed single-precision triangles of unspecified winding. Its vertices are the input
  // contour vertices cast to float (plus intersection points for self-intersecting input), so welding them back onto
  // the side ring by float position makes the solid watertight; each triangle is then oriented so the cap normal
  // points along +z for the far cap and -z for the near cap.
  private static void AddCap(
    Mesh2 cap,
    double z,
    bool counterClockwise,
    List<double> vertices,
    List<int> faces,
    Dictionary<(float X, float Y), (int Near, int Far)> ringIndexByPosition,
    bool far
  )
  {
    var extraIndexByPosition = new Dictionary<(float X, float Y), int>();
    int IndexOf(Vector2 p)
    {
      var key = ((float)p.X, (float)p.Y);
      if (ringIndexByPosition.TryGetValue(key, out var ring))
      {
        return far ? ring.Far : ring.Near;
      }
      if (!extraIndexByPosition.TryGetValue(key, out int index))
      {
        index = vertices.Count / 3;
        vertices.Add(p.X);
        vertices.Add(p.Y);
        vertices.Add(z);
        extraIndexByPosition[key] = index;
      }
      return index;
    }

    for (int i = 0; i < cap.Triangles.Count; i += 3)
    {
      var a = cap.Vertices[cap.Triangles[i]];
      var b = cap.Vertices[cap.Triangles[i + 1]];
      var c = cap.Vertices[cap.Triangles[i + 2]];
      double twiceArea = (b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y);
      if (Math.Abs(twiceArea) < PolygonMath.AREA_EPSILON)
      {
        continue;
      }
      if (twiceArea > 0 != counterClockwise)
      {
        (b, c) = (c, b);
      }
      faces.Add(3);
      faces.Add(IndexOf(a));
      faces.Add(IndexOf(b));
      faces.Add(IndexOf(c));
    }
  }
}
