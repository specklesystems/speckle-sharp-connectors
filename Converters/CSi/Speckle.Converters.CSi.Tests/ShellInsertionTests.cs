using Moq;
using NUnit.Framework;
using Speckle.Common.StructuralExtrusion;
using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.ToSpeckle.Helpers;
using Speckle.DoubleNumerics;
using Speckle.Objects.Geometry;

namespace Speckle.Converters.CSi.Tests;

public class ShellInsertionTests
{
  private const string SHELL = "synthetic-shell";
  private const string SECTION = "synthetic-section";
  private const string INSERTION_TABLE = "Area Assignments - Insertion Point";
  private const string THICKNESS_TABLE = "Area Assignments - Thickness Overwrites";
  private delegate int ReadProperty(string name, ref string section);
  private delegate int ReadOffsets(string name, ref int count, ref double[] offsets);
  private delegate int ReadMatrix(string name, ref double[] matrix, bool global);
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
  private delegate int ReadTables(
    ref int count,
    ref string[] keys,
    ref string[] names,
    ref int[] importTypes,
    ref bool[] empty
  );
  private delegate int ReadTable(
    string name,
    ref string[] requested,
    string group,
    ref int version,
    ref string[] fields,
    ref int count,
    ref string[] data
  );

  [TestCase(-100, -200, 0)]
  [TestCase(0, -100, 100)]
  [TestCase(100, 0, 200)]
  [TestCase(-60, -160, 40)]
  [TestCase(40, -60, 140)]
  [TestCase(140, 40, 240)]
  [TestCase(-130, -230, -30)]
  [TestCase(-30, -130, 70)]
  [TestCase(70, -30, 170)]
  public void Extrude_SavedNormalPlacement_PreservesSourceSurfaces(double displacement, double lower, double upper)
  {
    foreach (int orientation in new[] { 0, 1, 2 })
    {
      foreach (bool reverse in new[] { false, true })
      {
        var (normal, matrix, points) = Outline(orientation, reverse);
        var fixture = CreateFixture(Enumerable.Repeat(displacement, 4).ToArray(), matrix);
        var analytical = MeshOf(points);
        var originalVertices = analytical.vertices.ToArray();
        var originalFaces = analytical.faces.ToArray();
        var result = fixture.Extractor.TryExtrudeShell(new CsiShellWrapper { Name = SHELL }, analytical);
        Assert.That(result, Is.Not.Null);
        var mesh = result!;
        Assert.That(mesh.units, Is.EqualTo("mm"));
        var distances = Vertices(mesh).Select(p => Vector3.Dot(p - points[0], normal)).ToArray();
        Assert.That(distances.Min(), Is.EqualTo(lower).Within(1e-8));
        Assert.That(distances.Max(), Is.EqualTo(upper).Within(1e-8));
        Assert.That(distances, Has.All.Matches<double>(d => Math.Abs(d - lower) < 1e-8 || Math.Abs(d - upper) < 1e-8));
        foreach (var corner in points)
        {
          AssertVertex(mesh, corner + lower * normal);
          AssertVertex(mesh, corner + upper * normal);
        }
        AssertClosed(mesh);
        Assert.That(SignedVolume(mesh), Is.EqualTo(1_200_000).Within(1e-6));
        Assert.That(analytical.vertices, Is.EqualTo(originalVertices));
        Assert.That(analytical.faces, Is.EqualTo(originalFaces));
        Assert.That(fixture.Fallbacks.Total, Is.Zero);
      }
    }
  }

  [Test]
  public void Extrude_MiddleWithNoOffsets_PreservesExistingMeshExactly()
  {
    var (_, matrix, points) = Outline(0, false);
    var fixture = CreateFixture([0, 0, 0, 0], matrix);
    var mesh = fixture.Extractor.TryExtrudeShell(new CsiShellWrapper { Name = SHELL }, MeshOf(points));
    var prior = PrismBuilder.TryExtrudeOutline(points, 200)!;
    Assert.That(mesh, Is.Not.Null);
    Assert.That(mesh!.vertices, Is.EqualTo(prior.Vertices));
    Assert.That(mesh.faces, Is.EqualTo(prior.Faces));
  }

  [TestCase(false)]
  [TestCase(true)]
  public void Extrude_UnequalCornerOffsetsAndThickness_PreservesEveryPhysicalLayer(bool reverse)
  {
    var (_, matrix, points) = Outline(0, reverse);
    var offsets = reverse ? new double[] { -30, -40, -50, -60 } : [-60, -50, -40, -30];
    var overwrites = new TableRows(
      ["UniqueName", "PointNumber", "Thickness"],
      [SHELL, "1", "100", SHELL, "2", "0", SHELL, "3", "300", SHELL, "4", "400"]
    );
    var fixture = CreateFixture(offsets, matrix, thicknessRows: overwrites);
    var mesh = fixture.Extractor.TryExtrudeShell(new CsiShellWrapper { Name = SHELL }, MeshOf(points));
    Assert.That(mesh, Is.Not.Null);
    double[] lower = reverse ? [-80, -140, -200, -260] : [-110, -150, -190, -230];
    double[] upper = reverse ? [20, 60, 100, 140] : [-10, 50, 110, 170];
    for (int i = 0; i < 4; i++)
    {
      AssertVertex(mesh!, new Vector3(points[i].X, points[i].Y, lower[i]));
      AssertVertex(mesh!, new Vector3(points[i].X, points[i].Y, upper[i]));
    }
    AssertClosed(mesh!);
    Assert.That(fixture.Fallbacks.Total, Is.Zero);
  }

  [TestCase(false)]
  [TestCase(true)]
  public void Extrude_CurvedTopWithCornerVectorsAndThickness_InterpolatesSourceAssignments(bool reverse)
  {
    Vector3[] points = [new(0, 0, 0), new(4000, 0, 0), new(4000, 3000, 0), new(0, 3000, 0)];
    if (reverse)
    {
      Array.Reverse(points);
    }
    var insertionRows = new TableRows(
      ["UniqueName", "PointNumber", "CoordSys", "Offset1", "Offset2", "Offset3"],
      [
        SHELL,
        "1",
        "Global",
        "5",
        "7",
        "11",
        SHELL,
        "2",
        "",
        "10",
        "14",
        "22",
        SHELL,
        "3",
        "",
        "15",
        "21",
        "33",
        SHELL,
        "4",
        "",
        "20",
        "28",
        "44",
      ]
    );
    var thicknessRows = new TableRows(
      ["UniqueName", "PointNumber", "Thickness"],
      [SHELL, "1", "100", SHELL, "2", "0", SHELL, "3", "300", SHELL, "4", "400"]
    );
    var fixture = CreateFixture(
      [-89, -78, -67, -56],
      [1, 0, 0, 0, 1, 0, 0, 0, 1],
      insertionRows,
      thicknessRows,
      curve: new CurveData(3, [points[3], points[0], new(-1500, 1500, 0)])
    );
    var analytical = MeshOf(points);
    var originalVertices = analytical.vertices.ToArray();
    var originalFaces = analytical.faces.ToArray();

    var mesh = fixture.Extractor.TryExtrudeShell(new CsiShellWrapper { Name = SHELL }, analytical);

    Assert.That(mesh, Is.Not.Null);
    Assert.That(fixture.Fallbacks.Total, Is.Zero);
    Assert.That(analytical.vertices, Is.EqualTo(originalVertices));
    Assert.That(analytical.faces, Is.EqualTo(originalFaces));
    var vertices = Vertices(mesh!);
    int ringCount = vertices.Length / 2;
    Assert.That(ringCount, Is.GreaterThan(50));
    double[] lower = [-139, -178, -217, -256];
    double[] upper = [-39, 22, 83, 144];
    for (int i = 0; i < 4; i++)
    {
      var shift = new Vector3(5 * (i + 1), 7 * (i + 1), 0);
      Assert.That((vertices[i] - points[i] - shift - lower[i] * Vector3.UnitZ).Length(), Is.LessThan(1e-7));
      Assert.That((vertices[ringCount + i] - points[i] - shift - upper[i] * Vector3.UnitZ).Length(), Is.LessThan(1e-7));
    }
    AssertVertex(mesh!, new Vector3(-1487.5, 1517.5, -197.5));
    AssertVertex(mesh!, new Vector3(-1487.5, 1517.5, 52.5));
    for (int i = 4; i < ringCount; i++)
    {
      double fraction = (vertices[i].Z + 256) / 117;
      Assert.That(fraction, Is.InRange(0.0, 1.0));
      double angle = Math.PI * fraction;
      double y = 1500 + (reverse ? -1500 : 1500) * Math.Cos(angle);
      var expectedLower = new Vector3(
        -1500 * Math.Sin(angle) + 20 - 15 * fraction,
        y + 28 - 21 * fraction,
        -256 + 117 * fraction
      );
      var expectedUpper = new Vector3(expectedLower.X, expectedLower.Y, 144 - 183 * fraction);
      Assert.That((vertices[i] - expectedLower).Length(), Is.LessThan(1e-6));
      Assert.That((vertices[ringCount + i] - expectedUpper).Length(), Is.LessThan(1e-6));
      Assert.That(vertices[ringCount + i].Z - vertices[i].Z, Is.EqualTo(400 - 300 * fraction).Within(1e-6));
    }
    AssertClosed(mesh!);
    Assert.That(mesh!.units, Is.EqualTo("mm"));
  }

  [TestCase("Global")]
  [TestCase("Local")]
  public void Extrude_TangentialCornerOffsets_AreAppliedWithoutDoublingNormalOffsets(string system)
  {
    var (_, matrix, points) = Outline(2, false);
    var rows = new TableRows(
      ["UniqueName", "PointNumber", "CoordSys", "Offset1", "Offset2", "Offset3"],
      [SHELL, "1", system, "5", "7", "11"]
    );
    var fixture = CreateFixture([-111, -100, -100, -100], matrix, insertionRows: rows);
    var mesh = fixture.Extractor.TryExtrudeShell(new CsiShellWrapper { Name = SHELL }, MeshOf(points));
    Assert.That(mesh, Is.Not.Null);
    var expectedLower = system == "Global" ? new Vector3(5, 211, 11) : new Vector3(5, 211, 7);
    var expectedUpper = system == "Global" ? new Vector3(5, 11, 11) : new Vector3(5, 11, 7);
    AssertVertex(mesh!, expectedLower);
    AssertVertex(mesh!, expectedUpper);
    AssertClosed(mesh!);
  }

  [Test]
  public void Extrude_ConcaveCornerOffsets_PreservesCapCoverageAndClosedJunctions()
  {
    Vector3[] points = [new(0, 0, 0), new(100, 0, 0), new(100, 20, 0), new(30, 20, 0), new(30, 80, 0), new(0, 80, 0)];
    var fixture = CreateFixture([-100, -80, -60, -40, -20, 0], [1, 0, 0, 0, 1, 0, 0, 0, 1]);
    var mesh = fixture.Extractor.TryExtrudeShell(new CsiShellWrapper { Name = SHELL }, MeshOf(points));
    Assert.That(mesh, Is.Not.Null);
    AssertClosed(mesh!);
    Assert.That(SignedVolume(mesh!), Is.EqualTo(760_000).Within(1e-6));
    var capArea = CapProjectedArea(mesh!, points.Length);
    Assert.That(capArea, Is.EqualTo(3800).Within(1e-6));
  }

  [Test]
  public void Extrude_ContinuationOffsetRows_InheritTheSavedCoordinateSystem()
  {
    var (_, matrix, points) = Outline(0, false);
    var rows = new TableRows(
      ["UniqueName", "PointNumber", "CoordSys", "Offset1", "Offset2", "Offset3"],
      [
        SHELL,
        "1",
        "Local",
        "5",
        "7",
        "11",
        SHELL,
        "2",
        null!,
        "10",
        "14",
        "22",
        SHELL,
        "3",
        null!,
        "15",
        "21",
        "33",
        SHELL,
        "4",
        null!,
        "20",
        "28",
        "44",
      ]
    );
    var fixture = CreateFixture([-89, -78, -67, -56], matrix, insertionRows: rows);
    var mesh = fixture.Extractor.TryExtrudeShell(new CsiShellWrapper { Name = SHELL }, MeshOf(points));
    Assert.That(mesh, Is.Not.Null);
    Vector3[] lower = [new(5, 7, -189), new(110, 14, -178), new(115, 81, -167), new(20, 88, -156)];
    Vector3[] upper = [new(5, 7, 11), new(110, 14, 22), new(115, 81, 33), new(20, 88, 44)];
    foreach (var point in lower.Concat(upper))
    {
      AssertVertex(mesh!, point);
    }
    AssertClosed(mesh!);
  }

  [Test]
  public void Extrude_TangentialOffsetMakesBoundaryConcave_CapsCoverTheDisplacedBoundary()
  {
    Vector3[] points = [new(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0)];
    var rows = new TableRows(
      ["UniqueName", "PointNumber", "CoordSys", "Offset1", "Offset2", "Offset3"],
      [SHELL, "1", "Global", "7", "7", "0"]
    );
    var fixture = CreateFixture([0, 0, 0, 0], [1, 0, 0, 0, 1, 0, 0, 0, 1], insertionRows: rows);
    var mesh = fixture.Extractor.TryExtrudeShell(new CsiShellWrapper { Name = SHELL }, MeshOf(points));
    Assert.That(mesh, Is.Not.Null);
    AssertVertex(mesh!, new Vector3(7, 7, -100));
    AssertVertex(mesh!, new Vector3(7, 7, 100));
    Assert.That(CapProjectedArea(mesh!, 4), Is.EqualTo(30).Within(1e-8));
    Assert.That(SignedVolume(mesh!), Is.EqualTo(6000).Within(1e-8));
    AssertClosed(mesh!);
  }

  [Test]
  public void Extrude_DisplacedBoundaryIntersectsItself_FallsBack()
  {
    Vector3[] points = [new(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0)];
    var rows = new TableRows(
      ["UniqueName", "PointNumber", "CoordSys", "Offset1", "Offset2", "Offset3"],
      [SHELL, "1", "Global", "14", "7", "0"]
    );
    var fixture = CreateFixture([0, 0, 0, 0], [1, 0, 0, 0, 1, 0, 0, 0, 1], insertionRows: rows);
    var mesh = fixture.Extractor.TryExtrudeShell(new CsiShellWrapper { Name = SHELL }, MeshOf(points));
    Assert.That(mesh, Is.Null);
    Assert.That(fixture.Fallbacks.Total, Is.EqualTo(1));
  }

  [TestCase(1, 4, 4)]
  [TestCase(0, 3, 3)]
  [TestCase(0, 4, 3)]
  public void Extrude_InvalidInsertion_FallsBackInsteadOfCentering(int apiResult, int count, int length)
  {
    var (_, matrix, points) = Outline(0, false);
    var fixture = CreateFixture(new double[length], matrix, offsetResult: apiResult, offsetCount: count);
    var mesh = fixture.Extractor.TryExtrudeShell(new CsiShellWrapper { Name = SHELL }, MeshOf(points));
    Assert.That(mesh, Is.Null);
    Assert.That(fixture.Fallbacks.Total, Is.EqualTo(1));
  }

  [Test]
  public void Extrude_AssignmentTables_AreCapturedOncePerSend()
  {
    var (_, matrix, points) = Outline(0, false);
    var fixture = CreateFixture([0, 0, 0, 0], matrix);
    var shell = new CsiShellWrapper { Name = SHELL };
    Assert.That(fixture.Extractor.TryExtrudeShell(shell, MeshOf(points)), Is.Not.Null);
    Assert.That(fixture.Extractor.TryExtrudeShell(shell, MeshOf(points)), Is.Not.Null);
    fixture.Tables.Verify(
      x =>
        x.GetAllTables(
          ref It.Ref<int>.IsAny,
          ref It.Ref<string[]>.IsAny,
          ref It.Ref<string[]>.IsAny,
          ref It.Ref<int[]>.IsAny,
          ref It.Ref<bool[]>.IsAny
        ),
      Times.Once
    );
  }

  private sealed record TableRows(string[] Fields, string[] Data);

  private sealed record CurveData(int Edge, Vector3[] Controls);

  private sealed record Fixture(
    VolumetricDisplayValueExtractor Extractor,
    ExtrusionFallbackTracker Fallbacks,
    Mock<cDatabaseTables> Tables
  );

  private static Fixture CreateFixture(
    double[] offsets,
    double[] matrix,
    TableRows? insertionRows = null,
    TableRows? thicknessRows = null,
    int offsetResult = 0,
    int? offsetCount = null,
    CurveData? curve = null
  )
  {
    var areas = new Mock<cAreaObj>();
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
      .Setup(x => x.GetOffsets3(SHELL, ref It.Ref<int>.IsAny, ref It.Ref<double[]>.IsAny))
      .Returns(
        new ReadOffsets(
          (string _, ref int count, ref double[] value) =>
          {
            count = offsetCount ?? offsets.Length;
            value = offsets;
            return offsetResult;
          }
        )
      );
    areas
      .Setup(x => x.GetTransformationMatrix(SHELL, ref It.Ref<double[]>.IsAny, true))
      .Returns(
        new ReadMatrix(
          (string _, ref double[] value, bool global) =>
          {
            value = matrix;
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
            ref int[] types,
            ref double[] tension,
            ref int[] counts,
            ref double[] x,
            ref double[] y,
            ref double[] z
          ) =>
          {
            if (curve is null)
            {
              count = offsets.Length;
              types = new int[count];
              tension = new double[count];
              counts = new int[count];
              x = [];
              y = [];
              z = [];
              return 0;
            }
            count = offsets.Length;
            types = new int[count];
            types[curve.Edge] = 1;
            tension = new double[count];
            counts = new int[count];
            counts[curve.Edge] = curve.Controls.Length;
            x = curve.Controls.Select(p => p.X).ToArray();
            y = curve.Controls.Select(p => p.Y).ToArray();
            z = curve.Controls.Select(p => p.Z).ToArray();
            return 0;
          }
        )
      );
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
            keys = [INSERTION_TABLE, THICKNESS_TABLE];
            names = keys;
            types = [1, 1];
            empty = [insertionRows is null, thicknessRows is null];
            return 0;
          }
        )
      );
    tables
      .Setup(x =>
        x.GetTableForDisplayArray(
          It.IsAny<string>(),
          ref It.Ref<string[]>.IsAny,
          "",
          ref It.Ref<int>.IsAny,
          ref It.Ref<string[]>.IsAny,
          ref It.Ref<int>.IsAny,
          ref It.Ref<string[]>.IsAny
        )
      )
      .Returns(
        new ReadTable(
          (
            string name,
            ref string[] requested,
            string group,
            ref int version,
            ref string[] fields,
            ref int count,
            ref string[] data
          ) =>
          {
            var rows = name == INSERTION_TABLE ? insertionRows! : thicknessRows!;
            fields = rows.Fields;
            data = rows.Data;
            count = data.Length / fields.Length;
            return 0;
          }
        )
      );
    var model = new Mock<cSapModel>();
    model.SetupGet(x => x.AreaObj).Returns(areas.Object);
    model.SetupGet(x => x.DatabaseTables).Returns(tables.Object);
    var settings = new Mock<IConverterSettingsStore<CsiConversionSettings>>();
    settings.SetupGet(x => x.Current).Returns(new CsiConversionSettings(model.Object, "mm"));
    var thickness = new Mock<IShellThicknessResolver>();
    thickness.Setup(x => x.GetThickness(SECTION)).Returns(200);
    var fallbacks = new ExtrusionFallbackTracker();
    var cache = new CsiToSpeckleCacheSingleton();
    var resolver = new FrameSectionProfileResolver(
      settings.Object,
      cache,
      new FrameSectionAreaResolver(settings.Object, cache)
    );
    return new Fixture(
      new VolumetricDisplayValueExtractor(settings.Object, resolver, thickness.Object, fallbacks),
      fallbacks,
      tables
    );
  }

  private static (Vector3 Normal, double[] Matrix, Vector3[] Points) Outline(int orientation, bool reverse)
  {
    var (normal, x, y) = orientation switch
    {
      0 => (Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY),
      1 => (new Vector3(-0.6, 0, 0.8), new Vector3(0.8, 0, 0.6), Vector3.UnitY),
      _ => (-Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ),
    };
    Vector3[] points = [Vector3.Zero, x * 100, x * 100 + y * 60, y * 60];
    if (reverse)
    {
      Array.Reverse(points);
    }
    return (normal, [x.X, y.X, normal.X, x.Y, y.Y, normal.Y, x.Z, y.Z, normal.Z], points);
  }

  private static Mesh MeshOf(IReadOnlyList<Vector3> points) =>
    new()
    {
      vertices = points.SelectMany(p => new[] { p.X, p.Y, p.Z }).ToList(),
      faces = [points.Count, .. Enumerable.Range(0, points.Count)],
      units = "mm",
    };

  private static Vector3[] Vertices(Mesh mesh) =>
    Enumerable
      .Range(0, mesh.vertices.Count / 3)
      .Select(i => new Vector3(mesh.vertices[3 * i], mesh.vertices[3 * i + 1], mesh.vertices[3 * i + 2]))
      .ToArray();

  private static void AssertVertex(Mesh mesh, Vector3 expected) =>
    Assert.That(
      Vertices(mesh).Any(p => (p - expected).Length() < 1e-7),
      Is.True,
      $"Missing physical vertex {expected}"
    );

  private static double SignedVolume(Mesh mesh)
  {
    var vertices = Vertices(mesh);
    double result = 0;
    for (int i = 0; i < mesh.faces.Count; i += 4)
    {
      result +=
        Vector3.Dot(
          vertices[mesh.faces[i + 1]],
          Vector3.Cross(vertices[mesh.faces[i + 2]], vertices[mesh.faces[i + 3]])
        ) / 6;
    }
    return result;
  }

  private static double CapProjectedArea(Mesh mesh, int corners)
  {
    var vertices = Vertices(mesh);
    double result = 0;
    for (int i = 0; i < mesh.faces.Count; i += 4)
    {
      int a = mesh.faces[i + 1],
        b = mesh.faces[i + 2],
        c = mesh.faces[i + 3];
      if (a >= corners && b >= corners && c >= corners)
      {
        result += Math.Abs(Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).Z) / 2;
      }
    }
    return result;
  }

  private static void AssertClosed(Mesh mesh)
  {
    var edges = new Dictionary<(int, int), (int Count, int Orientation)>();
    for (int i = 0; i < mesh.faces.Count; i += 4)
    {
      Assert.That(mesh.faces[i], Is.EqualTo(3));
      for (int edge = 0; edge < 3; edge++)
      {
        int a = mesh.faces[i + 1 + edge],
          b = mesh.faces[i + 1 + (edge + 1) % 3];
        var key = a < b ? (a, b) : (b, a);
        edges.TryGetValue(key, out var value);
        edges[key] = (value.Count + 1, value.Orientation + (a < b ? 1 : -1));
      }
    }
    Assert.That(edges.Values.All(edge => edge.Count == 2 && edge.Orientation == 0), Is.True);
  }
}
