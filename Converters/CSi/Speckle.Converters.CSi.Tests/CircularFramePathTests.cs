using Moq;
using NUnit.Framework;
using Speckle.Common.StructuralExtrusion;
using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.ToSpeckle.Helpers;
using Speckle.DoubleNumerics;
using Speckle.Objects.Geometry;

namespace Speckle.Converters.CSi.Tests;

public class CircularFramePathTests
{
  private const string FRAME = "synthetic arc";
  private const string SECTION = "synthetic rectangle";
  private delegate int ReadCurve(
    string name,
    ref int type,
    ref double tension,
    ref int count,
    ref double[] x,
    ref double[] y,
    ref double[] z
  );
  private delegate int ReadSection(string name, ref string section, ref string autoSelect);
  private delegate int ReadInsertion(
    string name,
    ref int cardinal,
    ref bool mirror,
    ref bool stiff,
    ref double[] start,
    ref double[] end,
    ref string system
  );
  private delegate int ReadAxes(string name, ref double angle, ref bool advanced);

  private sealed record Source(Vector3[] Controls)
  {
    public int Type { get; init; } = 1;
    public int ReturnCode { get; init; }
    public double Angle { get; init; }
    public int Cardinal { get; init; } = 10;
    public Vector3 Offset { get; init; }
    public Vector3? EndOffset { get; init; }
    public string System { get; init; } = "Global";
    public string Units { get; init; } = "mm";
    public SectionProfile Profile { get; init; } = new RectangleProfile(80, 40);
  }

  [TestCase(1, false, 0, 10, "mm", false)]
  [TestCase(-1, false, 0, 10, "mm", false)]
  [TestCase(1, true, 0, 10, "mm", false)]
  [TestCase(-1, true, 45, 10, "mm", false)]
  [TestCase(1, false, 45, 10, "mm", false)]
  [TestCase(1, false, -45, 8, "mm", true)]
  [TestCase(1, false, 90, 10, "mm", false)]
  [TestCase(-1, true, 0, 8, "m", true)]
  [TestCase(1, false, 45, 10, "ft", false)]
  public void Extrude_CompleteRectangleSurfaceAndCapsFollowCircle(
    int side,
    bool reversed,
    double angle,
    int cardinal,
    string units,
    bool local
  )
  {
    double scale =
      units == "m" ? 0.001
      : units == "ft" ? 1 / 304.8
      : 1;
    double radius = 1000 * scale;
    var first = new Vector3(reversed ? radius : -radius, 0, 0);
    var offset = new Vector3(50, 80, -30) * scale;
    var source = new Source([first, -first, new(0, side * radius, 0)])
    {
      Angle = angle,
      Cardinal = cardinal,
      Offset = offset,
      System = local ? "Local" : "Global",
      Units = units,
      Profile = new RectangleProfile(80 * scale, 40 * scale),
    };
    var fixture = CreateFixture(source);
    double radians = angle * Math.PI / 180;
    int travel = side * (reversed ? 1 : -1);
    var chord = reversed ? -Vector3.UnitX : Vector3.UnitX;
    var local2 = Math.Cos(radians) * Vector3.UnitZ + Math.Sin(radians) * Vector3.Cross(chord, Vector3.UnitZ);
    var local3 = Vector3.Cross(chord, local2);
    var translation = local ? offset.X * chord + offset.Y * local2 + offset.Z * local3 : offset;
    double minDepth = (cardinal == 8 ? -80 : -40) * scale;
    double maxDepth = (cardinal == 8 ? 0 : 40) * scale;
    var axis = Axis(source);

    var mesh = fixture.Extractor.TryExtrudeFrame(new CsiFrameWrapper { Name = FRAME }, axis);

    Assert.That(mesh, Is.Not.Null);
    Assert.That(fixture.Fallbacks.Total, Is.Zero);
    Assert.That(mesh!.units, Is.EqualTo(units));
    Assert.That(new Vector3(axis.start.x, axis.start.y, axis.start.z), Is.EqualTo(first));
    Assert.That(new Vector3(axis.end.x, axis.end.y, axis.end.z), Is.EqualTo(-first));
    var capAtI = new List<Vector2>();
    var capAtJ = new List<Vector2>();
    foreach (var point in Vertices(mesh))
    {
      var relative = point - translation;
      var profile = Inverse(relative);
      Assert.That(profile.X, Is.InRange(minDepth - 1e-6 * scale, maxDepth + 1e-6 * scale));
      Assert.That(profile.Y, Is.InRange(-20 * scale - 1e-6 * scale, 20 * scale + 1e-6 * scale));
      Assert.That(side * relative.Y, Is.GreaterThanOrEqualTo(-1e-6 * scale));
      if (Math.Abs(relative.Y) < 1e-7 * scale)
      {
        (Vector3.Dot(relative, first) > 0 ? capAtI : capAtJ).Add(profile);
      }
    }
    foreach (var cap in new[] { capAtI, capAtJ })
    {
      Assert.That(cap, Has.Count.EqualTo(4));
      Assert.That(cap.Min(p => p.X), Is.EqualTo(minDepth).Within(1e-6 * scale));
      Assert.That(cap.Max(p => p.X), Is.EqualTo(maxDepth).Within(1e-6 * scale));
      Assert.That(cap.Min(p => p.Y), Is.EqualTo(-20 * scale).Within(1e-6 * scale));
      Assert.That(cap.Max(p => p.Y), Is.EqualTo(20 * scale).Within(1e-6 * scale));
    }
    double maxResidual = 0;
    foreach (var triangle in Triangles(mesh))
    {
      var a = triangle.A - translation;
      var b = triangle.B - translation;
      var c = triangle.C - translation;
      bool cap = Math.Max(Math.Abs(a.Y), Math.Max(Math.Abs(b.Y), Math.Abs(c.Y))) < 1e-7 * scale;
      if (cap)
      {
        continue;
      }
      foreach (
        var weights in new[]
        {
          new Vector3(0.5, 0.5, 0),
          new Vector3(0.25, 0.5, 0.25),
          new Vector3(1.0 / 3, 1.0 / 3, 1.0 / 3),
        }
      )
      {
        var p = Inverse(weights.X * a + weights.Y * b + weights.Z * c);
        double residual = Math.Min(
          Math.Min(Math.Abs(p.X - minDepth), Math.Abs(p.X - maxDepth)),
          Math.Min(Math.Abs(p.Y - 20 * scale), Math.Abs(p.Y + 20 * scale))
        );
        maxResidual = Math.Max(maxResidual, residual / scale);
      }
    }
    Assert.That(maxResidual, Is.LessThanOrEqualTo(0.25));
    AssertClosed(mesh);
    double shiftedRadius = radius + travel * ((minDepth + maxDepth) / 2) * Math.Sin(radians);
    Assert.That(
      SignedVolume(mesh),
      Is.EqualTo(80 * 40 * scale * scale * shiftedRadius * Math.PI)
        .Within(0.001 * 80 * 40 * scale * scale * shiftedRadius * Math.PI)
    );
    TestContext.Out.WriteLine(
      $"side={side}, reversed={reversed}, rotation={angle}, cardinal={cardinal}, units={units}, localOffset={local}: triangle sample residual {maxResidual:R} mm; both ordered cap domains and closure checked"
    );

    Vector2 Inverse(Vector3 point)
    {
      double delta = Math.Sqrt(point.X * point.X + point.Y * point.Y) - radius;
      double width = travel * delta;
      return new Vector2(
        Math.Cos(radians) * point.Z + Math.Sin(radians) * width,
        -Math.Sin(radians) * point.Z + Math.Cos(radians) * width
      );
    }
  }

  [TestCase(1)]
  [TestCase(-1)]
  public void Extrude_CircularControlsPreserveBothSidesAndEndSections(int side)
  {
    var fixture = CreateFixture(new Source([new(-1000, 0, 0), new(1000, 0, 0), new(0, side * 1000, 0)]));
    var axis = Axis();

    var mesh = fixture.Extractor.TryExtrudeFrame(new CsiFrameWrapper { Name = FRAME }, axis);

    Assert.That(mesh, Is.Not.Null);
    Assert.That(mesh!.vertices.Where((_, i) => i % 3 == 1).Max(y => side * y), Is.GreaterThan(999));
    Assert.That(fixture.Fallbacks.Total, Is.Zero);
    Assert.That(axis.start.x, Is.EqualTo(-1000));
    Assert.That(axis.end.x, Is.EqualTo(1000));
    foreach (var point in Vertices(mesh))
    {
      double radius = Math.Sqrt(point.X * point.X + point.Y * point.Y);
      Assert.That(radius, Is.InRange(980 - 1e-6, 1020 + 1e-6));
      Assert.That(point.Z, Is.InRange(-40 - 1e-6, 40 + 1e-6));
      Assert.That(side * point.Y, Is.GreaterThanOrEqualTo(-1e-6));
    }
  }

  [Test]
  public void Extrude_PopulatedUnsupportedCurveRecordsDiagnostic()
  {
    var fixture = CreateFixture(new Source([new(-1000, 0, 0), new(1000, 0, 0), new(0, 1000, 0)]) { Type = 2 });

    var mesh = fixture.Extractor.TryExtrudeFrame(new CsiFrameWrapper { Name = FRAME }, Axis());

    Assert.That(mesh, Is.Null);
    Assert.That(fixture.Fallbacks.Counts["FRAME/unsupported-curve"], Is.EqualTo(1));
  }

  [TestCase("controls")]
  [TestCase("collinear")]
  [TestCase("nonfinite")]
  [TestCase("endpoints")]
  [TestCase("read")]
  [TestCase("offsets")]
  [TestCase("rotation")]
  public void Extrude_ContradictoryCurveFallsBackWithReason(string pattern)
  {
    var source = new Source([new(-1000, 0, 0), new(1000, 0, 0), new(0, 1000, 0)]);
    source = pattern switch
    {
      "controls" => source with { Controls = source.Controls.Take(2).ToArray() },
      "collinear" => source with { Controls = [source.Controls[0], source.Controls[1], Vector3.Zero] },
      "nonfinite" => source with { Controls = [source.Controls[0], source.Controls[1], new(double.NaN, 1000, 0)] },
      "endpoints" => source with { Controls = [source.Controls[1], source.Controls[0], source.Controls[2]] },
      "read" => source with { ReturnCode = -99 },
      "offsets" => source with { EndOffset = new Vector3(0, 50, 0) },
      "rotation" => source with { Angle = double.NaN },
      _ => throw new ArgumentException(pattern),
    };
    string reason = pattern switch
    {
      "controls" => "invalid-curve-controls",
      "collinear" or "nonfinite" => "degenerate-curve-controls",
      "endpoints" => "curve-endpoint-mismatch",
      "read" => "curve-read-failed",
      "offsets" => "unsupported-curve-offsets",
      "rotation" => "degenerate-curve-sweep",
      _ => throw new ArgumentException(pattern),
    };
    var fixture = CreateFixture(source);

    var mesh = fixture.Extractor.TryExtrudeFrame(new CsiFrameWrapper { Name = FRAME }, Axis());

    Assert.That(mesh, Is.Null);
    Assert.That(fixture.Fallbacks.Counts[$"FRAME/{reason}"], Is.EqualTo(1));
    Assert.That(fixture.Fallbacks.Total, Is.EqualTo(1));
  }

  [TestCase(0)]
  [TestCase(1)]
  public void Extrude_EmptyCurveKeepsStraightPrism(int returnCode)
  {
    var fixture = CreateFixture(new Source([]) { Type = 0, ReturnCode = returnCode });

    var mesh = fixture.Extractor.TryExtrudeFrame(new CsiFrameWrapper { Name = FRAME }, Axis());

    Assert.That(mesh, Is.Not.Null);
    Assert.That(mesh!.vertices, Has.Count.EqualTo(24));
    Assert.That(Vertices(mesh).Min(p => p.Y), Is.EqualTo(-20));
    Assert.That(Vertices(mesh).Max(p => p.Y), Is.EqualTo(20));
    Assert.That(SignedVolume(mesh), Is.EqualTo(2000 * 80 * 40).Within(1e-6));
    Assert.That(fixture.Fallbacks.Total, Is.Zero);
  }

  [TestCase(false)]
  [TestCase(true)]
  public void Extrude_ConcaveAndHollowSectionsKeepInteriorWallsAndCapVoids(bool hollow)
  {
    SectionProfile profile = hollow
      ? new RectangularHollowProfile(80, 40, 8, 6)
      : new ISectionProfile(80, 40, 8, 6, 40, 8);
    var source = new Source([new(-1000, 0, 0), new(1000, 0, 0), new(0, 1000, 0)]) { Profile = profile };
    var fixture = CreateFixture(source);

    var mesh = fixture.Extractor.TryExtrudeFrame(new CsiFrameWrapper { Name = FRAME }, Axis());

    Assert.That(mesh, Is.Not.Null);
    AssertClosed(mesh!);
    double area = hollow ? 80 * 40 - 64 * 28 : 2 * 40 * 8 + 64 * 6;
    Assert.That(SignedVolume(mesh!), Is.EqualTo(area * Math.PI * 1000).Within(area * Math.PI));
    foreach (var triangle in Triangles(mesh!))
    {
      var center = (triangle.A + triangle.B + triangle.C) / 3;
      double depth = center.Z;
      double width = 1000 - Math.Sqrt(center.X * center.X + center.Y * center.Y);
      bool cap = Math.Max(Math.Abs(triangle.A.Y), Math.Max(Math.Abs(triangle.B.Y), Math.Abs(triangle.C.Y))) < 1e-7;
      if (cap)
      {
        bool inside = hollow
          ? Math.Abs(depth) >= 32 - 0.25 || Math.Abs(width) >= 14 - 0.25
          : Math.Abs(depth) >= 32 - 0.25 || Math.Abs(width) <= 3 + 0.25;
        Assert.That(inside, Is.True, "cap triangle covers a source void");
      }
      else
      {
        double[] distances = hollow
          ?
          [
            Math.Abs(Math.Abs(depth) - 40),
            Math.Abs(Math.Abs(depth) - 32),
            Math.Abs(Math.Abs(width) - 20),
            Math.Abs(Math.Abs(width) - 14),
          ]
          :
          [
            Math.Abs(Math.Abs(depth) - 40),
            Math.Abs(Math.Abs(depth) - 32),
            Math.Abs(Math.Abs(width) - 20),
            Math.Abs(Math.Abs(width) - 3),
          ];
        Assert.That(distances.Min(), Is.LessThanOrEqualTo(0.25));
      }
    }
    Assert.That(fixture.Fallbacks.Total, Is.Zero);
  }

  [Test]
  public void Extrude_ThroughPointChoosesMajorArc()
  {
    var source = new Source([new(1000, 0, 0), new(0, 1000, 0), new(-1000, 0, 0)]);
    var fixture = CreateFixture(source);

    var mesh = fixture.Extractor.TryExtrudeFrame(new CsiFrameWrapper { Name = FRAME }, Axis(source));

    Assert.That(mesh, Is.Not.Null);
    Assert.That(Vertices(mesh!).Min(p => p.X), Is.LessThan(-999));
    Assert.That(Vertices(mesh!).Min(p => p.Y), Is.LessThan(-999));
    Assert.That(SignedVolume(mesh!), Is.EqualTo(80 * 40 * 1000 * 1.5 * Math.PI).Within(80 * 40 * Math.PI));
    AssertClosed(mesh!);
    Assert.That(fixture.Fallbacks.Total, Is.Zero);
  }

  private static Line Axis() =>
    new()
    {
      start = new Point(-1000, 0, 0, "mm"),
      end = new Point(1000, 0, 0, "mm"),
      units = "mm",
    };

  private static Line Axis(Source source) =>
    new()
    {
      start = new Point(source.Controls[0].X, source.Controls[0].Y, source.Controls[0].Z, source.Units),
      end = new Point(source.Controls[1].X, source.Controls[1].Y, source.Controls[1].Z, source.Units),
      units = source.Units,
    };

  private static IEnumerable<(Vector3 A, Vector3 B, Vector3 C)> Triangles(Mesh mesh)
  {
    for (int i = 0; i < mesh.faces.Count; i += 4)
    {
      Assert.That(mesh.faces[i], Is.EqualTo(3));
      yield return (Vertex(mesh.faces[i + 1]), Vertex(mesh.faces[i + 2]), Vertex(mesh.faces[i + 3]));
    }
    Vector3 Vertex(int index) =>
      new(mesh.vertices[3 * index], mesh.vertices[3 * index + 1], mesh.vertices[3 * index + 2]);
  }

  private static double SignedVolume(Mesh mesh) =>
    Triangles(mesh).Sum(t => Vector3.Dot(t.A, Vector3.Cross(t.B, t.C)) / 6);

  private static void AssertClosed(Mesh mesh)
  {
    var edges = new Dictionary<(int, int), int>();
    for (int i = 0; i < mesh.faces.Count; i += 4)
    {
      int[] triangle = [mesh.faces[i + 1], mesh.faces[i + 2], mesh.faces[i + 3]];
      for (int j = 0; j < 3; j++)
      {
        int a = triangle[j],
          b = triangle[(j + 1) % 3];
        var edge = a < b ? (a, b) : (b, a);
        edges.TryGetValue(edge, out int count);
        edges[edge] = count + 1;
      }
    }
    Assert.That(edges.Values, Is.All.InRange(1, 2));
    var boundary = edges.Where(pair => pair.Value == 1).Select(pair => pair.Key).ToArray();
    var candidates = boundary.SelectMany(edge => new[] { edge.Item1, edge.Item2 }).Distinct().ToArray();
    var splitEdges = new Dictionary<(int, int), int>();
    foreach (var edge in boundary)
    {
      var start = Vertex(edge.Item1);
      var direction = Vertex(edge.Item2) - start;
      var splits = candidates
        .Select(index => (Index: index, T: Vector3.Dot(Vertex(index) - start, direction) / direction.LengthSquared()))
        .Where(point =>
          point.T >= -1e-9 && point.T <= 1 + 1e-9 && (Vertex(point.Index) - start - point.T * direction).Length() < 1e-7
        )
        .OrderBy(point => point.T)
        .ToArray();
      for (int i = 1; i < splits.Length; i++)
      {
        int a = splits[i - 1].Index,
          b = splits[i].Index;
        var key = a < b ? (a, b) : (b, a);
        splitEdges.TryGetValue(key, out int count);
        splitEdges[key] = count + 1;
      }
    }
    Assert.That(
      splitEdges.Values,
      Is.All.EqualTo(2),
      "geometric boundary remains open after cap T-junction edges are split"
    );

    Vector3 Vertex(int index) =>
      new(mesh.vertices[3 * index], mesh.vertices[3 * index + 1], mesh.vertices[3 * index + 2]);
  }

  private static IEnumerable<Vector3> Vertices(Mesh mesh)
  {
    for (int i = 0; i < mesh.vertices.Count; i += 3)
    {
      yield return new Vector3(mesh.vertices[i], mesh.vertices[i + 1], mesh.vertices[i + 2]);
    }
  }

  private sealed record Fixture(VolumetricDisplayValueExtractor Extractor, ExtrusionFallbackTracker Fallbacks);

  private static Fixture CreateFixture(Source source)
  {
    var frames = new Mock<cFrameObj>();
    frames
      .Setup(x => x.GetSection(FRAME, ref It.Ref<string>.IsAny, ref It.Ref<string>.IsAny))
      .Returns(
        new ReadSection(
          (string _, ref string section, ref string autoSelect) =>
          {
            section = SECTION;
            return 0;
          }
        )
      );
    frames
      .Setup(x =>
        x.GetCurved_2(
          FRAME,
          ref It.Ref<int>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<int>.IsAny,
          ref It.Ref<double[]>.IsAny,
          ref It.Ref<double[]>.IsAny,
          ref It.Ref<double[]>.IsAny
        )
      )
      .Returns(
        new ReadCurve(
          (
            string _,
            ref int curveType,
            ref double tension,
            ref int count,
            ref double[] x,
            ref double[] y,
            ref double[] z
          ) =>
          {
            curveType = source.Type;
            count = source.Controls.Length;
            x = source.Controls.Select(p => p.X).ToArray();
            y = source.Controls.Select(p => p.Y).ToArray();
            z = source.Controls.Select(p => p.Z).ToArray();
            return source.ReturnCode;
          }
        )
      );
    frames
      .Setup(x => x.GetLocalAxes(FRAME, ref It.Ref<double>.IsAny, ref It.Ref<bool>.IsAny))
      .Returns(
        new ReadAxes(
          (string _, ref double angle, ref bool advanced) =>
          {
            angle = source.Angle;
            return 0;
          }
        )
      );
    frames
      .Setup(x =>
        x.GetInsertionPoint(
          FRAME,
          ref It.Ref<int>.IsAny,
          ref It.Ref<bool>.IsAny,
          ref It.Ref<bool>.IsAny,
          ref It.Ref<double[]>.IsAny,
          ref It.Ref<double[]>.IsAny,
          ref It.Ref<string>.IsAny
        )
      )
      .Returns(
        new ReadInsertion(
          (
            string _,
            ref int cardinal,
            ref bool mirror,
            ref bool stiff,
            ref double[] start,
            ref double[] end,
            ref string system
          ) =>
          {
            cardinal = source.Cardinal;
            start = [source.Offset.X, source.Offset.Y, source.Offset.Z];
            var endOffset = source.EndOffset ?? source.Offset;
            end = [endOffset.X, endOffset.Y, endOffset.Z];
            system = source.System;
            return 0;
          }
        )
      );
    var model = new Mock<cSapModel>();
    model.SetupGet(x => x.FrameObj).Returns(frames.Object);
    var store = new ConverterSettingsStore<CsiConversionSettings>();
    store.Initialize(new CsiConversionSettings(model.Object, source.Units));
    var outline = SectionProfileCatalog.TryBuild(source.Profile)!;
    var cache = new CsiToSpeckleCacheSingleton();
    cache.FramePrismCache[SECTION] = new FrameSectionPrism(
      PrismBuilder.TryBuildUnitPrism(outline),
      outline,
      "Rectangular"
    );
    var resolver = new FrameSectionProfileResolver(store, cache, new FrameSectionAreaResolver(store, cache));
    var fallbacks = new ExtrusionFallbackTracker();
    return new Fixture(
      new VolumetricDisplayValueExtractor(store, resolver, Mock.Of<IShellThicknessResolver>(), fallbacks),
      fallbacks
    );
  }
}
