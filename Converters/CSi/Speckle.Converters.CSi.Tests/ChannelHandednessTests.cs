using Moq;
using NUnit.Framework;
using Speckle.Common.StructuralExtrusion;
using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.ToSpeckle.Helpers;
using Speckle.DoubleNumerics;
using Speckle.Objects.Geometry;

namespace Speckle.Converters.CSi.Tests;

public class ChannelHandednessTests
{
  private const double DEPTH = 127;
  private const double WIDTH = 48.006;
  private const double FLANGE = 8.128;
  private const double WEB = 8.255;
  private const string SECTION = "C5X9";
  private const string FRAME = "channel";

  private delegate int ReadType(string name, ref eFramePropType type);
  private delegate int ReadChannel(
    string name,
    ref string file,
    ref string material,
    ref double depth,
    ref double width,
    ref double flange,
    ref double web,
    ref int color,
    ref string notes,
    ref string guid
  );
  private delegate int ReadChannelMirror(
    string name,
    ref string file,
    ref string material,
    ref double depth,
    ref double width,
    ref double flange,
    ref double web,
    ref bool mirror,
    ref int color,
    ref string notes,
    ref string guid
  );
  private delegate int ReadArea(
    string name,
    ref double area,
    ref double as2,
    ref double as3,
    ref double torsion,
    ref double i22,
    ref double i33,
    ref double s22,
    ref double s33,
    ref double z22,
    ref double z33,
    ref double r22,
    ref double r33
  );
  private delegate int ReadSection(string name, ref string section, ref string autoSelect);
  private delegate int ReadAxes(string name, ref double angle, ref bool advanced);
  private delegate int ReadInsertion(
    string name,
    ref int cardinal,
    ref bool mirror,
    ref bool stiff,
    ref double[] start,
    ref double[] end,
    ref string system
  );
  private delegate int ReadRectangle(
    string name,
    ref string file,
    ref string material,
    ref double depth,
    ref double width,
    ref int color,
    ref string notes,
    ref string guid
  );
  private delegate int ReadISection(
    string name,
    ref string file,
    ref string material,
    ref double depth,
    ref double width,
    ref double flange,
    ref double web,
    ref double bottomWidth,
    ref double bottomFlange,
    ref double radius,
    ref int color,
    ref string notes,
    ref string guid
  );
  private delegate int ReadLegacyISection(
    string name,
    ref string file,
    ref string material,
    ref double depth,
    ref double width,
    ref double flange,
    ref double web,
    ref double bottomWidth,
    ref double bottomFlange,
    ref int color,
    ref string notes,
    ref string guid
  );

  [TestCase(false, false, false, 0)]
  [TestCase(false, true, false, 0)]
  [TestCase(false, false, false, 90)]
  [TestCase(false, false, true, 0)]
  [TestCase(false, true, false, 90)]
  [TestCase(false, false, true, 90)]
  [TestCase(true, false, false, 0)]
  [TestCase(true, true, false, 0)]
  [TestCase(true, false, false, 90)]
  [TestCase(true, false, true, 0)]
  [TestCase(true, true, false, 90)]
  [TestCase(true, false, true, 90)]
  public void Extrude_C5X9_WebFollowsSourceLocal3(bool mirrored, bool reversed, bool vertical, double angle)
  {
    var fixture = CreateFixture(mirrored, angle);
    var end = vertical ? new Vector3(0, 0, 1000) : new Vector3(reversed ? -1000 : 1000, 0, 0);
    var (local2, local3) = (vertical, reversed, angle) switch
    {
      (true, _, 90) => (Vector3.UnitY, -Vector3.UnitX),
      (true, _, 0) => (Vector3.UnitX, Vector3.UnitY),
      (false, true, 90) => (Vector3.UnitY, -Vector3.UnitZ),
      (false, false, 90) => (-Vector3.UnitY, -Vector3.UnitZ),
      (false, true, 0) => (Vector3.UnitZ, Vector3.UnitY),
      (false, false, 0) => (Vector3.UnitZ, -Vector3.UnitY),
      _ => throw new ArgumentOutOfRangeException(nameof(angle)),
    };
    var mesh = fixture.Extractor.TryExtrudeFrame(new CsiFrameWrapper { Name = FRAME }, Axis(end));
    Assert.That(mesh, Is.Not.Null);
    var sectionPoints = ProjectStart(mesh!, Vector3.Zero, local2, local3);
    var webWidths = WebWidths(sectionPoints);
    Assert.That(webWidths, Has.Length.EqualTo(2));
    double expectedMin = mirrored ? -WIDTH / 2 : WIDTH / 2 - WEB;
    double expectedMax = mirrored ? -WIDTH / 2 + WEB : WIDTH / 2;
    TestContext.Out.WriteLine(
      $"C5X9 mirror={mirrored}, reversed={reversed}, vertical={vertical}, angle={angle}: web local3 [{webWidths[0]:F3}, {webWidths[1]:F3}] mm; expected [{expectedMin:F3}, {expectedMax:F3}] mm"
    );
    Assert.That(webWidths[0], Is.EqualTo(expectedMin).Within(1e-6));
    Assert.That(webWidths[1], Is.EqualTo(expectedMax).Within(1e-6));
    Assert.That(sectionPoints.Min(p => p.X), Is.EqualTo(-DEPTH).Within(1e-6));
    Assert.That(sectionPoints.Max(p => p.X), Is.EqualTo(0).Within(1e-6));
    Assert.That(fixture.Fallbacks.Total, Is.Zero);
    Assert.That(SignedVolume(mesh!), Is.EqualTo((2 * WIDTH * FLANGE + (DEPTH - 2 * FLANGE) * WEB) * 1000).Within(1e-6));
    var cached = fixture.Resolver.Resolve(SECTION);
    Assert.That(fixture.Resolver.Resolve(SECTION), Is.SameAs(cached));
    VerifyChannelCalls(fixture.Properties, Times.Once());
  }

  [TestCase(false, 1)]
  [TestCase(false, 2)]
  [TestCase(false, 3)]
  [TestCase(false, 4)]
  [TestCase(false, 5)]
  [TestCase(false, 6)]
  [TestCase(false, 7)]
  [TestCase(false, 8)]
  [TestCase(false, 9)]
  [TestCase(false, 10)]
  [TestCase(true, 7)]
  [TestCase(true, 9)]
  [TestCase(true, 10)]
  public void Extrude_ChannelAnchorsUseOrientedBoundsAndEndOffsets(bool mirrored, int cardinal)
  {
    var fixture = CreateFixture(mirrored, cardinal: cardinal, startOffset: [5, 7, 11], endOffset: [9, -3, 17]);
    var mesh = fixture.Extractor.TryExtrudeFrame(new CsiFrameWrapper { Name = FRAME }, Axis(new Vector3(1000, 0, 0)));
    Assert.That(mesh, Is.Not.Null);
    double area = 2 * WIDTH * FLANGE + (DEPTH - 2 * FLANGE) * WEB;
    double centroid = WEB * (DEPTH - 2 * FLANGE) * (WIDTH - WEB) / (2 * area);
    double minDepth = cardinal == 10 ? -DEPTH / 2 : -(cardinal - 1) / 3 * DEPTH / 2;
    double minWidth =
      cardinal == 10 ? -WIDTH / 2 + (mirrored ? centroid : -centroid) : ((cardinal - 1) % 3 - 2) * WIDTH / 2;
    var startAnchor = new Vector3(5, -11, 7);
    var endAnchor = new Vector3(1009, -17, -3);
    var physicalAxis = Vector3.Normalize(endAnchor - startAnchor);
    var horizontal = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, physicalAxis));
    var depthAxis = Vector3.Cross(physicalAxis, horizontal);
    var widthAxis = -horizontal;
    var start = ProjectEnd(mesh!, depthAxis, widthAxis, false, startAnchor);
    var end = ProjectEnd(mesh!, depthAxis, widthAxis, true, endAnchor);
    Assert.That(start.Min(p => p.X), Is.EqualTo(minDepth).Within(1e-6));
    Assert.That(start.Min(p => p.Y), Is.EqualTo(minWidth).Within(1e-6));
    Assert.That(end.Min(p => p.X), Is.EqualTo(minDepth).Within(1e-6));
    Assert.That(end.Min(p => p.Y), Is.EqualTo(minWidth).Within(1e-6));
    double webMin = mirrored ? minWidth : minWidth + WIDTH - WEB;
    double webMax = mirrored ? minWidth + WEB : minWidth + WIDTH;
    var startWeb = WebWidths(start);
    var endWeb = WebWidths(end);
    Assert.That(startWeb, Has.Length.EqualTo(2));
    Assert.That(endWeb, Has.Length.EqualTo(2));
    Assert.That(startWeb[0], Is.EqualTo(webMin).Within(1e-6));
    Assert.That(startWeb[1], Is.EqualTo(webMax).Within(1e-6));
    Assert.That(endWeb[0], Is.EqualTo(webMin).Within(1e-6));
    Assert.That(endWeb[1], Is.EqualTo(webMax).Within(1e-6));
    Assert.That(SignedVolume(mesh!), Is.EqualTo(area * (endAnchor - startAnchor).Length()).Within(1e-6));
  }

  [Test]
  public void Resolve_FailedChannelGetterIsCachedAndFallsBack()
  {
    var fixture = CreateFixture(false, getterResult: 1);
    var section = fixture.Resolver.Resolve(SECTION);
    Assert.That(section.ShapeKey, Is.EqualTo("Channel"));
    Assert.That(section.Template, Is.Null);
    Assert.That(fixture.Resolver.Resolve(SECTION), Is.SameAs(section));
    Assert.That(
      fixture.Extractor.TryExtrudeFrame(new CsiFrameWrapper { Name = FRAME }, Axis(new Vector3(1000, 0, 0))),
      Is.Null
    );
    Assert.That(fixture.Fallbacks.Counts["FRAME/Channel"], Is.EqualTo(1));
    VerifyChannelCalls(fixture.Properties, Times.Once());
  }

  [Test]
  public void Resolve_InvalidDimensionsKeepFallbackReason()
  {
    var fixture = CreateFixture(false, depth: 0);
    var section = fixture.Resolver.Resolve(SECTION);
    Assert.That(section.Template, Is.Null);
    Assert.That(section.ShapeKey, Is.EqualTo("Channel/invalid-dimensions"));
  }

  [TestCase(false, false, 8)]
  [TestCase(false, true, 8)]
  [TestCase(true, false, 8)]
  [TestCase(true, true, 8)]
  [TestCase(false, false, 10)]
  [TestCase(false, true, 10)]
  [TestCase(true, false, 10)]
  [TestCase(true, true, 10)]
  public void Extrude_FrameInsertionMirrorReflectsAsymmetricSection(bool sectionMirror, bool frameMirror, int cardinal)
  {
    var fixture = CreateFixture(sectionMirror, cardinal: cardinal, frameMirrored: frameMirror);
    var mesh = fixture.Extractor.TryExtrudeFrame(new CsiFrameWrapper { Name = FRAME }, Axis(new Vector3(1000, 0, 0)));
    Assert.That(mesh, Is.Not.Null);
    var points = ProjectStart(mesh!, Vector3.Zero, Vector3.UnitZ, -Vector3.UnitY);
    double area = 2 * WIDTH * FLANGE + (DEPTH - 2 * FLANGE) * WEB;
    double centroid = WEB * (DEPTH - 2 * FLANGE) * (WIDTH - WEB) / (2 * area);
    bool reflected = sectionMirror != frameMirror;
    double minWidth = -WIDTH / 2 + (cardinal == 10 ? (reflected ? centroid : -centroid) : 0);
    var web = WebWidths(points);
    Assert.That(web[0], Is.EqualTo(reflected ? minWidth : minWidth + WIDTH - WEB).Within(1e-6));
    Assert.That(web[1], Is.EqualTo(reflected ? minWidth + WEB : minWidth + WIDTH).Within(1e-6));
    Assert.That(SignedVolume(mesh!), Is.EqualTo(area * 1000).Within(1e-6));
    AssertClosed(mesh!);
    Assert.That(fixture.Fallbacks.Total, Is.Zero);
  }

  [TestCase(false)]
  [TestCase(true)]
  public void Extrude_RootFilletedISectionPreservesSurfaceAndVolume(bool mirrored)
  {
    var fixture = CreateFixture(
      false,
      cardinal: 10,
      frameMirrored: mirrored,
      iSection: new ISectionProfile(190, 200, 10, 6.5, 200, 10, 18),
      hostArea: 5380,
      sectionName: "HEA200"
    );
    var mesh = fixture.Extractor.TryExtrudeFrame(new CsiFrameWrapper { Name = FRAME }, Axis(new Vector3(1000, 0, 0)));
    Assert.That(mesh, Is.Not.Null);
    var points = ProjectStart(mesh!, Vector3.Zero, Vector3.UnitZ, -Vector3.UnitY);
    Assert.That(points.Min(p => p.X), Is.EqualTo(-95).Within(1e-6));
    Assert.That(points.Max(p => p.X), Is.EqualTo(95).Within(1e-6));
    Assert.That(points.Min(p => p.Y), Is.EqualTo(-100).Within(1e-6));
    Assert.That(points.Max(p => p.Y), Is.EqualTo(100).Within(1e-6));
    var filletPoints = points.Where(p => p.X > 67 && p.X < 85 && p.Y > 3.25 && p.Y < 21.25).ToArray();
    Assert.That(filletPoints.Length, Is.GreaterThan(5));
    foreach (var point in filletPoints)
    {
      Assert.That(Vector2.Distance(point, new Vector2(67, 21.25)), Is.EqualTo(18).Within(1e-6));
    }
    for (int i = 0; i + 1 < filletPoints.Length; i++)
    {
      var midpoint = (filletPoints[i] + filletPoints[i + 1]) / 2;
      Assert.That(Math.Abs(18 - Vector2.Distance(midpoint, new Vector2(67, 21.25))), Is.LessThan(0.1));
    }
    double exactArea = 5105 + 4 * 18 * 18 * (1 - Math.PI / 4);
    Assert.That(SignedVolume(mesh!), Is.EqualTo(exactArea * 1000).Within(3000));
    AssertClosed(mesh!);
    Assert.That(fixture.Fallbacks.Total, Is.Zero);
  }

  [Test]
  public void Extrude_TypedISectionWinsOverChannelName()
  {
    var fixture = CreateFixture(
      false,
      cardinal: 10,
      frameMirrored: true,
      iSection: new ISectionProfile(500, 430, 90, 56, 430, 90, 15),
      hostArea: 95500,
      sectionName: "U 220"
    );
    var mesh = fixture.Extractor.TryExtrudeFrame(new CsiFrameWrapper { Name = FRAME }, Axis(new Vector3(1000, 0, 0)));
    Assert.That(mesh, Is.Not.Null);
    var points = ProjectStart(mesh!, Vector3.Zero, Vector3.UnitZ, -Vector3.UnitY);
    Assert.That(points.Max(p => p.X) - points.Min(p => p.X), Is.EqualTo(500).Within(1e-6));
    Assert.That(points.Max(p => p.Y) - points.Min(p => p.Y), Is.EqualTo(430).Within(1e-6));
    Assert.That(SignedVolume(mesh!), Is.EqualTo((95320 + 4 * 225 * (1 - Math.PI / 4)) * 1000).Within(2100));
    AssertClosed(mesh!);
    Assert.That(fixture.Resolver.Resolve("U 220").ShapeKey, Is.EqualTo("I"));
    Assert.That(fixture.Fallbacks.Total, Is.Zero);
  }

  [TestCase(4000)]
  [TestCase(7000)]
  public void Resolve_RootFilletDoesNotBypassInvalidSourceArea(double area)
  {
    var fixture = CreateFixture(false, iSection: new ISectionProfile(190, 200, 10, 6.5, 200, 10, 18), hostArea: area);
    Assert.That(fixture.Resolver.Resolve(SECTION).ShapeKey, Is.EqualTo("I/area-mismatch"));
    Assert.That(
      fixture.Extractor.TryExtrudeFrame(new CsiFrameWrapper { Name = FRAME }, Axis(new Vector3(1000, 0, 0))),
      Is.Null
    );
    Assert.That(fixture.Fallbacks.Counts["FRAME/I/area-mismatch"], Is.EqualTo(1));
  }

  [TestCase(-1)]
  [TestCase(90)]
  [TestCase(double.NaN)]
  public void Resolve_InvalidRootRadiusKeepsDimensionFallback(double radius)
  {
    var fixture = CreateFixture(
      false,
      iSection: new ISectionProfile(190, 200, 10, 6.5, 200, 10, radius),
      hostArea: 5380
    );
    Assert.That(fixture.Resolver.Resolve(SECTION).ShapeKey, Is.EqualTo("I/invalid-dimensions"));
  }

  [TestCase(0)]
  [TestCase(-99)]
  public void Extrude_UnfilletedISectionKeepsLegacyGetterCompatibility(int getterResult)
  {
    var fixture = CreateFixture(
      false,
      cardinal: 10,
      getterResult: getterResult,
      iSection: new ISectionProfile(190, 200, 10, 6.5, 200, 10),
      hostArea: 5105
    );
    var mesh = fixture.Extractor.TryExtrudeFrame(new CsiFrameWrapper { Name = FRAME }, Axis(new Vector3(1000, 0, 0)));
    Assert.That(mesh, Is.Not.Null);
    Assert.That(SignedVolume(mesh!), Is.EqualTo(5105000).Within(1e-6));
    AssertClosed(mesh!);
    Assert.That(fixture.Fallbacks.Total, Is.Zero);
  }

  [Test]
  public void Mirror_HollowOutlinePreservesVoidAndOutwardWinding()
  {
    var outline = ProfileOutline.TryCreate(
      [new(-2, -4), new(2, -4), new(2, 4), new(-2, 4)],
      [
        [new(-1, -3), new(1, -3), new(1, -1), new(-1, -1)],
      ]
    )!;
    var mirrored = outline.MirrorAboutDepth();
    Assert.That(mirrored.Area, Is.EqualTo(28).Within(1e-6));
    Assert.That(mirrored.Holes[0].Min(p => p.Y), Is.GreaterThan(0));
    var prism = PrismBuilder.TryBuildUnitPrism(mirrored)!;
    var mesh = new Mesh
    {
      vertices = prism.LocalVertices.ToList(),
      faces = prism.Faces.ToList(),
      units = "mm",
    };
    Assert.That(SignedVolume(mesh), Is.EqualTo(28).Within(1e-6));
    AssertClosed(mesh);
  }

  private static void AssertClosed(Mesh mesh)
  {
    var positions = new List<Vector3>();
    var vertexMap = new List<int>();
    for (int i = 0; i < mesh.vertices.Count; i += 3)
    {
      var point = new Vector3(mesh.vertices[i], mesh.vertices[i + 1], mesh.vertices[i + 2]);
      int index = positions.FindIndex(p => Vector3.Distance(p, point) < 1e-5);
      if (index < 0)
      {
        index = positions.Count;
        positions.Add(point);
      }
      vertexMap.Add(index);
    }
    var edges = new Dictionary<(int, int), (int Count, int Winding)>();
    for (int i = 0; i < mesh.faces.Count; i += 4)
    {
      for (int j = 0; j < 3; j++)
      {
        int a = vertexMap[mesh.faces[i + 1 + j]],
          b = vertexMap[mesh.faces[i + 1 + (j + 1) % 3]];
        var direction = positions[b] - positions[a];
        var split = positions
          .Select((p, index) => (p, index, t: Vector3.Dot(p - positions[a], direction) / direction.LengthSquared()))
          .Where(p => p.t >= -1e-8 && p.t <= 1 + 1e-8 && Vector3.Distance(p.p, positions[a] + p.t * direction) < 1e-5)
          .OrderBy(p => p.t)
          .Select(p => p.index)
          .ToArray();
        for (int k = 0; k + 1 < split.Length; k++)
        {
          int first = split[k],
            second = split[k + 1];
          var edge = (Math.Min(first, second), Math.Max(first, second));
          var previous = edges.GetValueOrDefault(edge);
          edges[edge] = (previous.Count + 1, previous.Winding + (first < second ? 1 : -1));
        }
      }
    }
    Assert.That(edges.Values.Select(e => e.Count), Is.All.EqualTo(2));
    Assert.That(edges.Values.Select(e => e.Winding), Is.All.Zero);
  }

  [Test]
  public void Resolve_SharedChannelConventionIsUnchanged()
  {
    var outline = SectionProfileCatalog.TryBuild(new ChannelProfile(DEPTH, WIDTH, FLANGE, WEB));
    Assert.That(outline, Is.Not.Null);
    Assert.That(outline!.MinWidth + outline.MaxWidth, Is.GreaterThan(0));
  }

  [Test]
  public void Extrude_SymmetricRectangleKeepsItsBounds()
  {
    var fixture = CreateFixture(false, rectangle: true);
    var mesh = fixture.Extractor.TryExtrudeFrame(new CsiFrameWrapper { Name = FRAME }, Axis(new Vector3(1000, 0, 0)));
    Assert.That(mesh, Is.Not.Null);
    var points = ProjectStart(mesh!, Vector3.Zero, Vector3.UnitZ, -Vector3.UnitY);
    Assert.That(points.Min(p => p.X), Is.EqualTo(-DEPTH).Within(1e-6));
    Assert.That(points.Max(p => p.X), Is.EqualTo(0).Within(1e-6));
    Assert.That(points.Min(p => p.Y), Is.EqualTo(-WIDTH / 2).Within(1e-6));
    Assert.That(points.Max(p => p.Y), Is.EqualTo(WIDTH / 2).Within(1e-6));
    VerifyChannelCalls(fixture.Properties, Times.Never());
  }

  private static void VerifyChannelCalls(Mock<cPropFrame> properties, Times times)
  {
    properties.Verify(
      x =>
        x.GetChannel_1(
          SECTION,
          ref It.Ref<string>.IsAny,
          ref It.Ref<string>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<bool>.IsAny,
          ref It.Ref<int>.IsAny,
          ref It.Ref<string>.IsAny,
          ref It.Ref<string>.IsAny
        ),
      times
    );
    properties.Verify(
      x =>
        x.GetChannel(
          SECTION,
          ref It.Ref<string>.IsAny,
          ref It.Ref<string>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<int>.IsAny,
          ref It.Ref<string>.IsAny,
          ref It.Ref<string>.IsAny
        ),
      Times.Never()
    );
  }

  private static double SignedVolume(Mesh mesh)
  {
    double volume = 0;
    Vector3 Vertex(int index) =>
      new(mesh.vertices[3 * index], mesh.vertices[3 * index + 1], mesh.vertices[3 * index + 2]);
    for (int i = 0; i < mesh.faces.Count; i += 4)
    {
      Assert.That(mesh.faces[i], Is.EqualTo(3));
      volume +=
        Vector3.Dot(Vertex(mesh.faces[i + 1]), Vector3.Cross(Vertex(mesh.faces[i + 2]), Vertex(mesh.faces[i + 3]))) / 6;
    }
    return volume;
  }

  private static List<Vector2> ProjectEnd(Mesh mesh, Vector3 local2, Vector3 local3, bool end, Vector3 anchor)
  {
    var points = new List<Vector2>();
    int first = end ? mesh.vertices.Count / 2 : 0;
    for (int i = first; i < first + mesh.vertices.Count / 2; i += 3)
    {
      var point = new Vector3(mesh.vertices[i], mesh.vertices[i + 1], mesh.vertices[i + 2]) - anchor;
      points.Add(new Vector2(Vector3.Dot(point, local2), Vector3.Dot(point, local3)));
    }
    return points;
  }

  private static Line Axis(Vector3 end) =>
    new()
    {
      start = new Point(0, 0, 0, "mm"),
      end = new Point(end.X, end.Y, end.Z, "mm"),
      units = "mm",
    };

  private static List<Vector2> ProjectStart(Mesh mesh, Vector3 origin, Vector3 local2, Vector3 local3)
  {
    var points = new List<Vector2>();
    for (int i = 0; i < mesh.vertices.Count / 2; i += 3)
    {
      var point = new Vector3(mesh.vertices[i], mesh.vertices[i + 1], mesh.vertices[i + 2]) - origin;
      points.Add(new Vector2(Vector3.Dot(point, local2), Vector3.Dot(point, local3)));
    }
    return points;
  }

  private static double[] WebWidths(IReadOnlyList<Vector2> points)
  {
    var widths = new List<double>();
    double slice = (points.Min(p => p.X) + points.Max(p => p.X)) / 2;
    for (int i = 0; i < points.Count; i++)
    {
      var start = points[i];
      var end = points[(i + 1) % points.Count];
      if ((start.X < slice && end.X > slice) || (start.X > slice && end.X < slice))
      {
        widths.Add(start.Y + (end.Y - start.Y) * (slice - start.X) / (end.X - start.X));
      }
    }
    return widths.Order().ToArray();
  }

  private sealed record Fixture(
    VolumetricDisplayValueExtractor Extractor,
    FrameSectionProfileResolver Resolver,
    ExtrusionFallbackTracker Fallbacks,
    Mock<cPropFrame> Properties
  );

  private static Fixture CreateFixture(
    bool mirrored,
    double angle = 0,
    int cardinal = 8,
    double[]? startOffset = null,
    double[]? endOffset = null,
    int getterResult = 0,
    double depth = DEPTH,
    bool frameMirrored = false,
    bool rectangle = false,
    ISectionProfile? iSection = null,
    double? hostArea = null,
    string sectionName = SECTION
  )
  {
    var properties = new Mock<cPropFrame>(MockBehavior.Strict);
    properties
      .Setup(x => x.GetTypeOAPI(sectionName, ref It.Ref<eFramePropType>.IsAny))
      .Returns(
        new ReadType(
          (string _, ref eFramePropType type) =>
          {
            type =
              rectangle ? eFramePropType.Rectangular
              : iSection is null ? eFramePropType.Channel
              : eFramePropType.I;
            return 0;
          }
        )
      );
    properties
      .Setup(x =>
        x.GetISection_1(
          sectionName,
          ref It.Ref<string>.IsAny,
          ref It.Ref<string>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<int>.IsAny,
          ref It.Ref<string>.IsAny,
          ref It.Ref<string>.IsAny
        )
      )
      .Returns(
        new ReadISection(
          (
            string _,
            ref string file,
            ref string material,
            ref double depth,
            ref double width,
            ref double flange,
            ref double web,
            ref double bottomWidth,
            ref double bottomFlange,
            ref double radius,
            ref int color,
            ref string notes,
            ref string guid
          ) =>
          {
            depth = iSection!.Depth;
            width = iSection.TopFlangeWidth;
            flange = iSection.TopFlangeThickness;
            web = iSection.WebThickness;
            bottomWidth = iSection.BottomFlangeWidth;
            bottomFlange = iSection.BottomFlangeThickness;
            radius = iSection.RootRadius;
            return getterResult;
          }
        )
      );
    properties
      .Setup(x =>
        x.GetISection(
          sectionName,
          ref It.Ref<string>.IsAny,
          ref It.Ref<string>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<int>.IsAny,
          ref It.Ref<string>.IsAny,
          ref It.Ref<string>.IsAny
        )
      )
      .Returns(
        new ReadLegacyISection(
          (
            string _,
            ref string file,
            ref string material,
            ref double depth,
            ref double width,
            ref double flange,
            ref double web,
            ref double bottomWidth,
            ref double bottomFlange,
            ref int color,
            ref string notes,
            ref string guid
          ) =>
          {
            depth = iSection!.Depth;
            width = iSection.TopFlangeWidth;
            flange = iSection.TopFlangeThickness;
            web = iSection.WebThickness;
            bottomWidth = iSection.BottomFlangeWidth;
            bottomFlange = iSection.BottomFlangeThickness;
            return 0;
          }
        )
      );
    properties
      .Setup(x =>
        x.GetChannel(
          sectionName,
          ref It.Ref<string>.IsAny,
          ref It.Ref<string>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<int>.IsAny,
          ref It.Ref<string>.IsAny,
          ref It.Ref<string>.IsAny
        )
      )
      .Returns(
        new ReadChannel(
          (
            string _,
            ref string file,
            ref string material,
            ref double depth,
            ref double width,
            ref double flange,
            ref double web,
            ref int color,
            ref string notes,
            ref string guid
          ) =>
          {
            depth = DEPTH;
            width = WIDTH;
            flange = FLANGE;
            web = WEB;
            return 0;
          }
        )
      );
    properties
      .Setup(x =>
        x.GetChannel_1(
          sectionName,
          ref It.Ref<string>.IsAny,
          ref It.Ref<string>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<bool>.IsAny,
          ref It.Ref<int>.IsAny,
          ref It.Ref<string>.IsAny,
          ref It.Ref<string>.IsAny
        )
      )
      .Returns(
        new ReadChannelMirror(
          (
            string _,
            ref string file,
            ref string material,
            ref double resultDepth,
            ref double width,
            ref double flange,
            ref double web,
            ref bool mirror,
            ref int color,
            ref string notes,
            ref string guid
          ) =>
          {
            resultDepth = depth;
            width = WIDTH;
            flange = FLANGE;
            web = WEB;
            mirror = mirrored;
            return getterResult;
          }
        )
      );
    properties
      .Setup(x =>
        x.GetRectangle(
          sectionName,
          ref It.Ref<string>.IsAny,
          ref It.Ref<string>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<int>.IsAny,
          ref It.Ref<string>.IsAny,
          ref It.Ref<string>.IsAny
        )
      )
      .Returns(
        new ReadRectangle(
          (
            string _,
            ref string file,
            ref string material,
            ref double resultDepth,
            ref double width,
            ref int color,
            ref string notes,
            ref string guid
          ) =>
          {
            resultDepth = DEPTH;
            width = WIDTH;
            return 0;
          }
        )
      );
    properties
      .Setup(x =>
        x.GetSectProps(
          sectionName,
          ref It.Ref<double>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<double>.IsAny
        )
      )
      .Returns(
        new ReadArea(
          (
            string _,
            ref double area,
            ref double as2,
            ref double as3,
            ref double torsion,
            ref double i22,
            ref double i33,
            ref double s22,
            ref double s33,
            ref double z22,
            ref double z33,
            ref double r22,
            ref double r33
          ) =>
          {
            area = hostArea ?? (rectangle ? DEPTH * WIDTH : 1703.2);
            return 0;
          }
        )
      );
    var frames = new Mock<cFrameObj>();
    frames
      .Setup(x => x.GetSection(FRAME, ref It.Ref<string>.IsAny, ref It.Ref<string>.IsAny))
      .Returns(
        new ReadSection(
          (string _, ref string section, ref string autoSelect) =>
          {
            section = sectionName;
            return 0;
          }
        )
      );
    frames
      .Setup(x => x.GetLocalAxes(FRAME, ref It.Ref<double>.IsAny, ref It.Ref<bool>.IsAny))
      .Returns(
        new ReadAxes(
          (string _, ref double result, ref bool advanced) =>
          {
            result = angle;
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
            ref int resultCardinal,
            ref bool mirror,
            ref bool stiff,
            ref double[] start,
            ref double[] end,
            ref string system
          ) =>
          {
            resultCardinal = cardinal;
            mirror = frameMirrored;
            start = startOffset ?? [];
            end = endOffset ?? [];
            system = "Local";
            return 0;
          }
        )
      );
    var model = new Mock<cSapModel>();
    model.SetupGet(x => x.PropFrame).Returns(properties.Object);
    model.SetupGet(x => x.FrameObj).Returns(frames.Object);
    var store = new ConverterSettingsStore<CsiConversionSettings>();
    store.Initialize(new CsiConversionSettings(model.Object, "mm"));
    var cache = new CsiToSpeckleCacheSingleton();
    var resolver = new FrameSectionProfileResolver(store, cache, new FrameSectionAreaResolver(store, cache));
    var fallbacks = new ExtrusionFallbackTracker();
    var extractor = new VolumetricDisplayValueExtractor(store, resolver, Mock.Of<IShellThicknessResolver>(), fallbacks);
    return new Fixture(extractor, resolver, fallbacks, properties);
  }
}
