using Moq;
using NUnit.Framework;
using Speckle.Common.StructuralExtrusion;
using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.ToSpeckle.Helpers;
using Speckle.DoubleNumerics;
using Speckle.Objects.Geometry;
using Speckle.Sdk.Common;

namespace Speckle.Converters.CSi.Tests;

public class CurvedShellTests
{
  private const string SHELL = "synthetic shell";
  private const string SECTION = "synthetic slab";
  private delegate int ReadOpening(string name, ref bool opening);
  private delegate int ReadProperty(string name, ref string property);
  private delegate int ReadOffsets(string name, ref int count, ref double[] offsets);
  private delegate int ReadMatrix(string name, ref double[] matrix, bool global);
  private delegate int ReadTables(
    ref int count,
    ref string[] keys,
    ref string[] names,
    ref int[] importTypes,
    ref bool[] empty
  );
  private delegate int ReadCurves(
    string name,
    ref int count,
    ref int[] types,
    ref double[] tension,
    ref int[] points,
    ref double[] x,
    ref double[] y,
    ref double[] z
  );

  [TestCase(-1, false, false, false)]
  [TestCase(1, false, false, false)]
  [TestCase(-1, true, false, false)]
  [TestCase(1, true, false, false)]
  [TestCase(-1, true, true, true)]
  [TestCase(1, false, true, true)]
  [TestCase(1, true, true, false)]
  [TestCase(-1, false, false, true)]
  public void Extrude_CircularEdgePreservesCompleteCapsAndSides(int side, bool sloped, bool reversed, bool closing)
  {
    Vector3[] corners = [new(0, 0, 0), new(4000, 0, 0), new(4000, 3000, 0), new(0, 3000, 0)];
    var through = Map(new Vector3(2000, side * 2000, 0));
    var ordered = closing ? new[] { corners[1], corners[2], corners[3], corners[0] } : corners;
    if (reversed)
    {
      ordered = ordered.Reverse().ToArray();
    }
    int edge = Array.IndexOf(ordered, reversed ? corners[1] : corners[0]);
    var types = new int[4];
    var counts = new int[4];
    types[edge] = 1;
    counts[edge] = 3;
    var fixture = CreateFixture(types, counts, [Map(corners[0]), Map(corners[1]), through], sloped: sloped);
    var outline = Outline(ordered.Select(Map));
    var originalVertices = outline.vertices.ToArray();

    var mesh = fixture.Extractor.TryExtrudeShell(new CsiShellWrapper { Name = SHELL }, outline);

    Assert.That(mesh, Is.Not.Null);
    Assert.That(fixture.Fallbacks.Total, Is.Zero);
    Assert.That(outline.vertices, Is.EqualTo(originalVertices));
    var normal = sloped ? new Vector3(0, -0.6, 0.8) : Vector3.UnitZ;
    double expectedArea = 12000000 - side * Math.PI * 2000 * 2000 / 2;
    Assert.That(CapArea(mesh!, normal), Is.EqualTo(expectedArea).Within(2500));
    Assert.That(SignedVolume(mesh!), Is.EqualTo(expectedArea * 200).Within(500000));
    var ring = BoundaryAt(mesh!, normal);
    for (int sample = 0; sample <= 200; sample++)
    {
      double angle = Math.PI * sample / 200;
      var expected = Map(new Vector3(2000 - 2000 * Math.Cos(angle), side * 2000 * Math.Sin(angle), 0));
      Assert.That(DistanceToRing(expected - 100 * normal, ring), Is.LessThanOrEqualTo(0.251));
    }
    Assert.That(ring.Count, Is.GreaterThan(50));
    Assert.That(ring.Any(p => (p - (through - 100 * normal)).Length() < 1e-6), Is.True);
    Assert.That(mesh!.units, Is.EqualTo("mm"));
    AssertClosed(mesh);

    Vector3 Map(Vector3 p) => sloped ? new Vector3(p.X + 500, 0.8 * p.Y - 200, 0.6 * p.Y + 900) : p;
  }

  [Test]
  public void Extrude_MajorArcFollowsThroughPoint()
  {
    Vector3[] corners = [new(0, 0, 0), new(4000, 0, 0), new(4000, 3000, 0), new(0, 3000, 0)];
    var fixture = CreateFixture([1, 0, 0, 0], [3, 0, 0, 0], [corners[0], corners[1], new(2000, -6000, 0)]);

    var mesh = fixture.Extractor.TryExtrudeShell(new CsiShellWrapper { Name = SHELL }, Outline(corners));

    Assert.That(mesh, Is.Not.Null);
    double radius = 10000.0 / 3;
    double sweep = 2 * Math.PI - 2 * Math.Asin(0.6);
    double area = 12000000 + radius * radius * (sweep - Math.Sin(sweep)) / 2;
    Assert.That(CapArea(mesh!, Vector3.UnitZ), Is.EqualTo(area).Within(4000));
    Assert.That(SignedVolume(mesh!), Is.EqualTo(area * 200).Within(800000));
    var ring = BoundaryAt(mesh!, Vector3.UnitZ);
    for (int i = 0; i <= 300; i++)
    {
      double angle = Math.PI / 2 + Math.Asin(0.6) + sweep * i / 300;
      var point = new Vector3(2000 + radius * Math.Cos(angle), -8000.0 / 3 + radius * Math.Sin(angle), -100);
      Assert.That(DistanceToRing(point, ring), Is.LessThanOrEqualTo(0.251));
    }
    AssertClosed(mesh!);
  }

  [TestCase(2999)]
  [TestCase(3001)]
  [TestCase(4000)]
  public void Extrude_CrossingCurveWithUniformAssignmentsFallsBack(double throughY)
  {
    Vector3[] corners = [new(0, 0, 0), new(4000, 0, 0), new(4000, 3000, 0), new(0, 3000, 0)];
    var fixture = CreateFixture([1, 0, 0, 0], [3, 0, 0, 0], [corners[0], corners[1], new(2000, throughY, 0)]);

    var mesh = fixture.Extractor.TryExtrudeShell(new CsiShellWrapper { Name = SHELL }, Outline(corners));

    Assert.That(mesh, Is.Null);
    Assert.That(fixture.Fallbacks.Counts["SHELL/degenerate-outline"], Is.EqualTo(1));
  }

  [Test]
  public void Extrude_StraightControlRetainsPreviousGeometry()
  {
    Vector3[] corners = [new(10, 20, 30), new(4010, 20, 30), new(4010, 3020, 30), new(10, 3020, 30)];
    var fixture = CreateFixture([0, 0, 0, 0], [0, 0, 0, 0], []);
    var expected = PrismBuilder.TryExtrudeOutline(corners, 200)!;

    var actual = fixture.Extractor.TryExtrudeShell(new CsiShellWrapper { Name = SHELL }, Outline(corners));

    Assert.That(actual, Is.Not.Null);
    Assert.That(actual!.vertices, Is.EqualTo(expected.Vertices));
    Assert.That(actual.faces, Is.EqualTo(expected.Faces));
    Assert.That(fixture.Fallbacks.Total, Is.Zero);
  }

  [TestCase(0, "invalid-curve-data")]
  [TestCase(1, "curve-read-failed")]
  [TestCase(2, "curve-read-failed")]
  public void Extrude_EmptyCurveResponseFallsBack(int result, string reason)
  {
    Vector3[] corners = [new(0, 0, 0), new(4000, 0, 0), new(4000, 3000, 0), new(0, 3000, 0)];
    var fixture = CreateFixture([], [], [], curveResult: result);

    var actual = fixture.Extractor.TryExtrudeShell(new CsiShellWrapper { Name = SHELL }, Outline(corners));

    Assert.That(actual, Is.Null);
    Assert.That(fixture.Fallbacks.Counts["SHELL/" + reason], Is.EqualTo(1));
  }

  [Test]
  public void Extrude_MultipleCurvesUseTheirOrderedFlattenedControls()
  {
    Vector3[] corners = [new(0, 0, 0), new(4000, 0, 0), new(4000, 3000, 0), new(0, 3000, 0)];
    Vector3[] controls =
    [
      corners[0],
      corners[1],
      new(2000, -1000, 0),
      corners[1],
      corners[2],
      new(5000, 1500, 0),
      corners[3],
      corners[0],
      new(-1000, 1500, 0),
    ];
    var fixture = CreateFixture([1, 1, 0, 1], [3, 3, 0, 3], controls);

    var mesh = fixture.Extractor.TryExtrudeShell(new CsiShellWrapper { Name = SHELL }, Outline(corners));

    Assert.That(mesh, Is.Not.Null);
    double bottomSweep = 2 * Math.Asin(0.8);
    double sideSweep = 2 * Math.Asin(1500.0 / 1625);
    double area =
      12000000
      + 2500 * 2500 * (bottomSweep - Math.Sin(bottomSweep)) / 2
      + 1625 * 1625 * (sideSweep - Math.Sin(sideSweep));
    Assert.That(CapArea(mesh!, Vector3.UnitZ), Is.EqualTo(area).Within(4000));
    foreach (int control in new[] { 2, 5, 8 })
    {
      Assert.That(
        DistanceToRing(controls[control] - 100 * Vector3.UnitZ, BoundaryAt(mesh!, Vector3.UnitZ)),
        Is.LessThan(1e-6)
      );
    }
    Assert.That(fixture.Fallbacks.Total, Is.Zero);
    AssertClosed(mesh!);
  }

  [Test]
  public void Extrude_TessellationLimitHasExplicitFallback()
  {
    Vector3[] corners = [new(0, 0, 0), new(4e9, 0, 0), new(4e9, 3e9, 0), new(0, 3e9, 0)];
    var fixture = CreateFixture([1, 0, 0, 0], [3, 0, 0, 0], [corners[0], corners[1], new(2e9, -2e9, 0)]);

    var mesh = fixture.Extractor.TryExtrudeShell(new CsiShellWrapper { Name = SHELL }, Outline(corners));

    Assert.That(mesh, Is.Null);
    Assert.That(fixture.Fallbacks.Counts["SHELL/curve-tessellation-limit"], Is.EqualTo(1));
  }

  [TestCase(0.9)]
  [TestCase(1.570796326794895)]
  public void Boundary_ThroughPointDoesNotAddDuplicateSamplingVertex(double throughAngle)
  {
    Vector3[] corners = [new(0, 0, 0), new(4000, 0, 0), new(4000, 3000, 0), new(0, 3000, 0)];
    var through = new Vector3(2000 - 2000 * Math.Cos(throughAngle), -2000 * Math.Sin(throughAngle), 0);
    var fixture = CreateFixture([1, 0, 0, 0], [3, 0, 0, 0], [corners[0], corners[1], through]);

    var boundary = ShellBoundaryReader.TryRead(fixture.Areas, SHELL, corners, "mm", out string failure);

    Assert.That(boundary, Is.Not.Null);
    Assert.That(failure, Is.Empty);
    Assert.That(boundary!.Points, Does.Contain(through));
    for (int i = 0; i < boundary.Points.Count; i++)
    {
      Assert.That(
        (boundary.Points[i] - boundary.Points[(i + 1) % boundary.Points.Count]).Length(),
        Is.GreaterThan(1e-6)
      );
    }
  }

  [TestCase(2, 3, 0, "unsupported-curve-type")]
  [TestCase(3, 3, 0, "unsupported-curve-type")]
  [TestCase(4, 3, 0, "unsupported-curve-type")]
  [TestCase(1, 2, 0, "invalid-curve-data")]
  [TestCase(0, 3, 0, "invalid-curve-data")]
  [TestCase(1, 3, 1, "curve-read-failed")]
  public void Extrude_UnreadableOrUnsupportedCurvesHaveExplicitFallback(int type, int count, int result, string reason)
  {
    Vector3[] corners = [new(0, 0, 0), new(4000, 0, 0), new(4000, 3000, 0), new(0, 3000, 0)];
    var fixture = CreateFixture(
      [type, 0, 0, 0],
      [count, 0, 0, 0],
      [corners[0], corners[1], new(2000, -2000, 0)],
      result
    );

    var mesh = fixture.Extractor.TryExtrudeShell(new CsiShellWrapper { Name = SHELL }, Outline(corners));

    Assert.That(mesh, Is.Null);
    Assert.That(fixture.Fallbacks.Counts["SHELL/" + reason], Is.EqualTo(1));
  }

  [TestCase(double.NaN, "invalid-curve-data")]
  [TestCase(double.PositiveInfinity, "invalid-curve-data")]
  [TestCase(0, "degenerate-circular-edge")]
  public void Extrude_InvalidCircularControlsHaveExplicitFallback(double throughY, string reason)
  {
    Vector3[] corners = [new(0, 0, 0), new(4000, 0, 0), new(4000, 3000, 0), new(0, 3000, 0)];
    var fixture = CreateFixture([1, 0, 0, 0], [3, 0, 0, 0], [corners[0], corners[1], new(2000, throughY, 0)]);

    var mesh = fixture.Extractor.TryExtrudeShell(new CsiShellWrapper { Name = SHELL }, Outline(corners));

    Assert.That(mesh, Is.Null);
    Assert.That(fixture.Fallbacks.Counts["SHELL/" + reason], Is.EqualTo(1));
  }

  [Test]
  public void Extrude_OffPlaneCurveDoesNotProjectToSuccessfulShell()
  {
    Vector3[] corners = [new(0, 0, 0), new(4000, 0, 0), new(4000, 3000, 0), new(0, 3000, 0)];
    var fixture = CreateFixture([1, 0, 0, 0], [3, 0, 0, 0], [corners[0], corners[1], new(2000, -2000, 100)]);

    var mesh = fixture.Extractor.TryExtrudeShell(new CsiShellWrapper { Name = SHELL }, Outline(corners));

    Assert.That(mesh, Is.Null);
    Assert.That(fixture.Fallbacks.Counts["SHELL/nonplanar-curved-boundary"], Is.EqualTo(1));
  }

  [TestCase("mm")]
  [TestCase("cm")]
  [TestCase("m")]
  [TestCase("in")]
  [TestCase("ft")]
  public void Extrude_ChordToleranceUsesDeclaredLengthUnits(string units)
  {
    double scale = Units.GetConversionFactor("mm", units);
    Vector3[] corners = [new(0, 0, 0), new(4000, 0, 0), new(4000, 3000, 0), new(0, 3000, 0)];
    corners = corners.Select(p => p * scale).ToArray();
    var fixture = CreateFixture(
      [1, 0, 0, 0],
      [3, 0, 0, 0],
      [corners[0], corners[1], new Vector3(2000, -2000, 0) * scale],
      units: units,
      thickness: 200 * scale
    );

    var mesh = fixture.Extractor.TryExtrudeShell(new CsiShellWrapper { Name = SHELL }, Outline(corners));

    Assert.That(mesh, Is.Not.Null);
    var ring = BoundaryAt(mesh!, Vector3.UnitZ).Select(p => p / scale).ToList();
    for (int i = 0; i <= 200; i++)
    {
      double angle = Math.PI * i / 200;
      var point = new Vector3(2000 - 2000 * Math.Cos(angle), -2000 * Math.Sin(angle), -100);
      Assert.That(DistanceToRing(point, ring), Is.LessThanOrEqualTo(0.251));
    }
    Assert.That(mesh!.units, Is.EqualTo(units));
  }

  [Test]
  public void Boundary_ActiveCornersReplaceStaleEndpointsAndRetainAssignmentProvenance()
  {
    Vector3[] corners = [new(0, 0, 0), new(4000, 0, 0), new(4000, 3000, 0), new(0, 3000, 0)];
    var fixture = CreateFixture([0, 0, 0, 1], [0, 0, 0, 3], [new(-50, 2990, 0), new(10, -20, 0), new(-1500, 1500, 0)]);
    var boundary = ShellBoundaryReader.TryRead(fixture.Areas, SHELL, corners, "mm", out string failure);

    Assert.That(boundary, Is.Not.Null);
    Assert.That(failure, Is.Empty);
    Vector3[] offsets = [new(10, 20, 30), new(40, 50, 60), new(70, 80, 90), new(100, 110, 120)];
    double[] thicknesses = [200, 300, 400, 500];
    var displacements = boundary!.Interpolate(offsets);
    var denseThickness = boundary.Interpolate(thicknesses);
    Assert.That(boundary.Points.Take(4), Is.EqualTo(corners));
    Assert.That(displacements.Take(4), Is.EqualTo(offsets));
    Assert.That(denseThickness.Take(4), Is.EqualTo(thicknesses));
    int through = Enumerable
      .Range(0, boundary.Points.Count)
      .Single(i => boundary.Points[i] == new Vector3(-1500, 1500, 0));
    Assert.That(displacements[through], Is.EqualTo(new Vector3(55, 65, 75)));
    Assert.That(denseThickness[through], Is.EqualTo(350));
    for (int i = 4; i < boundary.Points.Count; i++)
    {
      var point = boundary.Points[i];
      double angle = Math.Atan2(1500 - point.Y, -point.X);
      double fraction = (angle + Math.PI / 2) / Math.PI;
      Assert.That((displacements[i] - (offsets[3] + fraction * (offsets[0] - offsets[3]))).Length(), Is.LessThan(1e-6));
      Assert.That(denseThickness[i], Is.EqualTo(500 - 300 * fraction).Within(1e-6));
    }
    Assert.That(() => boundary.Interpolate(new double[3]), Throws.ArgumentException);
    Assert.That(() => boundary.Interpolate(new Vector3[3]), Throws.ArgumentException);
  }

  private static List<Vector3> BoundaryAt(Mesh mesh, Vector3 normal)
  {
    var points = Vertices(mesh);
    double plane = points.Min(p => Vector3.Dot(p, normal));
    return points.Where(p => Math.Abs(Vector3.Dot(p, normal) - plane) < 1e-6).ToList();
  }

  private static double DistanceToRing(Vector3 point, IReadOnlyList<Vector3> ring)
  {
    double distance = double.PositiveInfinity;
    for (int i = 0; i < ring.Count; i++)
    {
      var a = ring[i];
      var delta = ring[(i + 1) % ring.Count] - a;
      double t = Math.Clamp(Vector3.Dot(point - a, delta) / delta.LengthSquared(), 0, 1);
      distance = Math.Min(distance, (point - a - t * delta).Length());
    }
    return distance;
  }

  private static List<Vector3> Vertices(Mesh mesh) =>
    Enumerable
      .Range(0, mesh.vertices.Count / 3)
      .Select(i => new Vector3(mesh.vertices[3 * i], mesh.vertices[3 * i + 1], mesh.vertices[3 * i + 2]))
      .ToList();

  private static double CapArea(Mesh mesh, Vector3 normal)
  {
    var vertices = Vertices(mesh);
    double area = 0;
    for (int i = 0; i < mesh.faces.Count; i += 4)
    {
      var a = vertices[mesh.faces[i + 1]];
      var b = vertices[mesh.faces[i + 2]];
      var c = vertices[mesh.faces[i + 3]];
      if (Math.Abs(Vector3.Dot(b - a, normal)) < 1e-6 && Math.Abs(Vector3.Dot(c - a, normal)) < 1e-6)
      {
        area += Vector3.Cross(b - a, c - a).Length() / 4;
      }
    }
    return area;
  }

  private static double SignedVolume(Mesh mesh)
  {
    var vertices = Vertices(mesh);
    double volume = 0;
    for (int i = 0; i < mesh.faces.Count; i += 4)
    {
      volume +=
        Vector3.Dot(
          vertices[mesh.faces[i + 1]],
          Vector3.Cross(vertices[mesh.faces[i + 2]], vertices[mesh.faces[i + 3]])
        ) / 6;
    }
    return volume;
  }

  private static void AssertClosed(Mesh mesh)
  {
    var edges = new Dictionary<(int, int), int>();
    for (int i = 0; i < mesh.faces.Count; i += 4)
    {
      for (int j = 0; j < 3; j++)
      {
        int a = mesh.faces[i + 1 + j];
        int b = mesh.faces[i + 1 + (j + 1) % 3];
        var key = a < b ? (a, b) : (b, a);
        edges.TryGetValue(key, out int count);
        edges[key] = count + 1;
      }
    }
    Assert.That(edges.Values, Is.All.EqualTo(2));
  }

  private static Mesh Outline(IEnumerable<Vector3> points) =>
    new()
    {
      vertices = points.SelectMany(p => new[] { p.X, p.Y, p.Z }).ToList(),
      faces = [],
      units = "mm",
    };

  private sealed record Fixture(
    VolumetricDisplayValueExtractor Extractor,
    ExtrusionFallbackTracker Fallbacks,
    cAreaObj Areas
  );

  private static Fixture CreateFixture(
    int[] types,
    int[] counts,
    Vector3[] controls,
    int curveResult = 0,
    string units = "mm",
    double thickness = 200,
    bool sloped = false
  )
  {
    var areas = new Mock<cAreaObj>();
    areas
      .Setup(x => x.GetOffsets3(SHELL, ref It.Ref<int>.IsAny, ref It.Ref<double[]>.IsAny))
      .Returns(
        new ReadOffsets(
          (string _, ref int count, ref double[] offsets) =>
          {
            count = 4;
            offsets = new double[4];
            return 0;
          }
        )
      );
    areas
      .Setup(x => x.GetTransformationMatrix(SHELL, ref It.Ref<double[]>.IsAny, true))
      .Returns(
        new ReadMatrix(
          (string _, ref double[] matrix, bool global) =>
          {
            matrix = sloped ? [1, 0, 0, 0, 0.8, -0.6, 0, 0.6, 0.8] : [1, 0, 0, 0, 1, 0, 0, 0, 1];
            return 0;
          }
        )
      );
    areas
      .Setup(x => x.GetOpening(SHELL, ref It.Ref<bool>.IsAny))
      .Returns(new ReadOpening((string _, ref bool opening) => 0));
    areas
      .Setup(x => x.GetProperty(SHELL, ref It.Ref<string>.IsAny))
      .Returns(
        new ReadProperty(
          (string _, ref string section) =>
          {
            section = SECTION;
            return 0;
          }
        )
      );
    areas
      .Setup(x =>
        x.GetCurvedEdges(
          SHELL,
          ref It.Ref<int>.IsAny,
          ref It.Ref<int[]>.IsAny,
          ref It.Ref<double[]>.IsAny,
          ref It.Ref<int[]>.IsAny,
          ref It.Ref<double[]>.IsAny,
          ref It.Ref<double[]>.IsAny,
          ref It.Ref<double[]>.IsAny
        )
      )
      .Returns(
        new ReadCurves(
          (
            string _,
            ref int count,
            ref int[] resultTypes,
            ref double[] tension,
            ref int[] resultCounts,
            ref double[] x,
            ref double[] y,
            ref double[] z
          ) =>
          {
            count = types.Length;
            resultTypes = types;
            tension = new double[types.Length];
            resultCounts = counts;
            x = controls.Select(p => p.X).ToArray();
            y = controls.Select(p => p.Y).ToArray();
            z = controls.Select(p => p.Z).ToArray();
            return curveResult;
          }
        )
      );
    var model = new Mock<cSapModel>();
    model.SetupGet(x => x.AreaObj).Returns(areas.Object);
    var tables = new Mock<cDatabaseTables>();
    tables
      .Setup(x =>
        x.GetAllTables(
          ref It.Ref<int>.IsAny,
          ref It.Ref<string[]>.IsAny,
          ref It.Ref<string[]>.IsAny,
          ref It.Ref<int[]>.IsAny,
          ref It.Ref<bool[]>.IsAny
        )
      )
      .Returns(
        new ReadTables(
          (ref int count, ref string[] keys, ref string[] names, ref int[] types, ref bool[] empty) =>
          {
            count = 2;
            keys = ["Area Assignments - Insertion Point", "Area Assignments - Thickness Overwrites"];
            names = keys;
            types = [1, 1];
            empty = [true, true];
            return 0;
          }
        )
      );
    model.SetupGet(x => x.DatabaseTables).Returns(tables.Object);
    var store = new ConverterSettingsStore<CsiConversionSettings>();
    store.Initialize(new CsiConversionSettings(model.Object, units, SendVolumetricGeometry: true));
    var cache = new CsiToSpeckleCacheSingleton();
    var resolver = new FrameSectionProfileResolver(store, cache, new FrameSectionAreaResolver(store, cache));
    var thicknessResolver = new Mock<IShellThicknessResolver>();
    thicknessResolver.Setup(x => x.GetThickness(SECTION)).Returns(thickness);
    var fallbacks = new ExtrusionFallbackTracker();
    return new Fixture(
      new VolumetricDisplayValueExtractor(store, resolver, thicknessResolver.Object, fallbacks),
      fallbacks,
      areas.Object
    );
  }
}
