using Moq;
using NUnit.Framework;
using Speckle.Common.StructuralExtrusion;
using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.ToSpeckle.Helpers;
using Speckle.DoubleNumerics;
using Speckle.Objects.Geometry;

namespace Speckle.Converters.CSi.Tests;

public class PhysicalFrameAxisTests
{
  private const string SECTION = "synthetic rectangle";
  private const string FRAME = "synthetic frame";
  private const double DEPTH = 800;
  private const double WIDTH = 400;
  private const double TOLERANCE = 1e-6;

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

  [TestCase(0, false, false, false)]
  [TestCase(-45, false, false, false)]
  [TestCase(90, false, false, false)]
  [TestCase(0, true, false, false)]
  [TestCase(-45, true, false, false)]
  [TestCase(0, false, true, false)]
  [TestCase(-45, false, true, false)]
  [TestCase(-45, true, true, false)]
  [TestCase(0, false, false, true)]
  [TestCase(-45, true, false, true)]
  [TestCase(-45, false, true, true)]
  public void Extrude_DisplacedAnchorsHavePerpendicularCaps(double angle, bool reversed, bool vertical, bool local)
  {
    var analyticalZ = (vertical ? Vector3.UnitZ : Vector3.UnitX) * (reversed ? -1 : 1);
    var local2 = vertical ? Vector3.UnitX : Vector3.UnitZ;
    var local3 = Vector3.Cross(analyticalZ, local2);
    double radians = angle * Math.PI / 180;
    var rotated2 = Math.Cos(radians) * local2 + Math.Sin(radians) * local3;
    var rotated3 = -Math.Sin(radians) * local2 + Math.Cos(radians) * local3;
    double[] startOffset = [120, -75, 40];
    double[] endOffset = [340, 160, -90];
    var sourceStart = new Vector3(20, 30, 40);
    var sourceEnd = sourceStart + 1500 * analyticalZ;
    var start = sourceStart + Offset(startOffset);
    var end = sourceEnd + Offset(endOffset);
    var physicalZ = Vector3.Normalize(end - start);
    var horizontal = Vector3.Normalize(Vector3.Cross(rotated2, physicalZ));
    var depthAxis = Vector3.Cross(physicalZ, horizontal);
    var widthAxis = -horizontal;
    var fixture = CreateFixture(angle, 8, startOffset, endOffset, local ? "Local" : "Global");
    var axis = Axis(sourceStart, sourceEnd);

    var mesh = fixture.Extractor.TryExtrudeFrame(new CsiFrameWrapper { Name = FRAME }, axis);

    Assert.That(mesh, Is.Not.Null);
    AssertCap(mesh!, 0, start, depthAxis, widthAxis, physicalZ, -DEPTH, 0, -WIDTH / 2, WIDTH / 2);
    AssertCap(mesh!, 4, end, depthAxis, widthAxis, physicalZ, -DEPTH, 0, -WIDTH / 2, WIDTH / 2);
    Assert.That(SignedVolume(mesh!), Is.EqualTo(DEPTH * WIDTH * (end - start).Length()).Within(1e-3));
    Assert.That(fixture.Fallbacks.Total, Is.Zero);
    Assert.That(new Vector3(axis.start.x, axis.start.y, axis.start.z), Is.EqualTo(sourceStart));
    Assert.That(new Vector3(axis.end.x, axis.end.y, axis.end.z), Is.EqualTo(sourceEnd));
    TestContext.Out.WriteLine($"angle={angle}, reversed={reversed}, vertical={vertical}, local={local}: both caps perpendicular; ordered anchors and 800 x 400 mm profile verified");

    Vector3 Offset(double[] values) => local
      ? values[0] * analyticalZ + values[1] * rotated2 + values[2] * rotated3
      : new Vector3(values[0], values[1], values[2]);
  }

  [TestCase(0)]
  [TestCase(-45)]
  public void Extrude_EqualOffsetsKeepAnalyticalBasis(double angle)
  {
    double radians = angle * Math.PI / 180;
    var depthAxis = new Vector3(0, -Math.Sin(radians), Math.Cos(radians));
    var widthAxis = new Vector3(0, -Math.Cos(radians), -Math.Sin(radians));
    var fixture = CreateFixture(angle, 10, [50, 80, -30], [50, 80, -30], "Global");

    var mesh = fixture.Extractor.TryExtrudeFrame(new CsiFrameWrapper { Name = FRAME }, Axis(Vector3.Zero, new Vector3(1500, 0, 0)));

    Assert.That(mesh, Is.Not.Null);
    AssertCap(mesh!, 0, new Vector3(50, 80, -30), depthAxis, widthAxis, Vector3.UnitX, -DEPTH / 2, DEPTH / 2, -WIDTH / 2, WIDTH / 2);
    AssertCap(mesh!, 4, new Vector3(1550, 80, -30), depthAxis, widthAxis, Vector3.UnitX, -DEPTH / 2, DEPTH / 2, -WIDTH / 2, WIDTH / 2);
    Assert.That(SignedVolume(mesh!), Is.EqualTo(DEPTH * WIDTH * 1500).Within(1e-3));
  }

  [Test]
  public void Extrude_CoincidentPhysicalAnchorsFallBack()
  {
    var fixture = CreateFixture(0, 10, [], [-1500, 0, 0], "Global");

    var mesh = fixture.Extractor.TryExtrudeFrame(new CsiFrameWrapper { Name = FRAME }, Axis(Vector3.Zero, new Vector3(1500, 0, 0)));

    Assert.That(mesh, Is.Null);
    Assert.That(fixture.Fallbacks.Counts["FRAME/zero-length"], Is.EqualTo(1));
  }

  private static void AssertCap(Mesh mesh, int first, Vector3 anchor, Vector3 depthAxis, Vector3 widthAxis, Vector3 physicalZ, double minDepth, double maxDepth, double minWidth, double maxWidth)
  {
    var cap = Enumerable.Range(first, 4).Select(index => Vertex(mesh, index) - anchor).ToArray();
    var axial = cap.Select(point => Vector3.Dot(point, physicalZ)).ToArray();
    Assert.That(axial.Max() - axial.Min(), Is.LessThanOrEqualTo(TOLERANCE));
    Assert.That(axial, Is.All.EqualTo(0).Within(TOLERANCE));
    Assert.That(cap.Min(point => Vector3.Dot(point, depthAxis)), Is.EqualTo(minDepth).Within(TOLERANCE));
    Assert.That(cap.Max(point => Vector3.Dot(point, depthAxis)), Is.EqualTo(maxDepth).Within(TOLERANCE));
    Assert.That(cap.Min(point => Vector3.Dot(point, widthAxis)), Is.EqualTo(minWidth).Within(TOLERANCE));
    Assert.That(cap.Max(point => Vector3.Dot(point, widthAxis)), Is.EqualTo(maxWidth).Within(TOLERANCE));
  }

  private static Vector3 Vertex(Mesh mesh, int index) => new(mesh.vertices[3 * index], mesh.vertices[3 * index + 1], mesh.vertices[3 * index + 2]);

  private static double SignedVolume(Mesh mesh)
  {
    double volume = 0;
    for (int i = 0; i < mesh.faces.Count; i += 4)
    {
      Assert.That(mesh.faces[i], Is.EqualTo(3));
      volume += Vector3.Dot(Vertex(mesh, mesh.faces[i + 1]), Vector3.Cross(Vertex(mesh, mesh.faces[i + 2]), Vertex(mesh, mesh.faces[i + 3]))) / 6;
    }
    return volume;
  }

  private static Line Axis(Vector3 start, Vector3 end) => new()
  {
    start = new Point(start.X, start.Y, start.Z, "mm"),
    end = new Point(end.X, end.Y, end.Z, "mm"),
    units = "mm",
  };

  private sealed record Fixture(VolumetricDisplayValueExtractor Extractor, ExtrusionFallbackTracker Fallbacks);

  private static Fixture CreateFixture(double angle, int cardinal, double[] startOffset, double[] endOffset, string system)
  {
    var frames = new Mock<cFrameObj>();
    frames.Setup(x => x.GetSection(FRAME, ref It.Ref<string>.IsAny, ref It.Ref<string>.IsAny)).Returns(new ReadSection((string _, ref string section, ref string autoSelect) =>
    {
      section = SECTION;
      return 0;
    }));
    frames.Setup(x => x.GetLocalAxes(FRAME, ref It.Ref<double>.IsAny, ref It.Ref<bool>.IsAny)).Returns(new ReadAxes((string _, ref double result, ref bool advanced) =>
    {
      result = angle;
      return 0;
    }));
    frames.Setup(x => x.GetInsertionPoint(FRAME, ref It.Ref<int>.IsAny, ref It.Ref<bool>.IsAny, ref It.Ref<bool>.IsAny, ref It.Ref<double[]>.IsAny, ref It.Ref<double[]>.IsAny, ref It.Ref<string>.IsAny)).Returns(new ReadInsertion((string _, ref int resultCardinal, ref bool mirror, ref bool stiff, ref double[] start, ref double[] end, ref string resultSystem) =>
    {
      resultCardinal = cardinal;
      start = startOffset;
      end = endOffset;
      resultSystem = system;
      return 0;
    }));
    var model = new Mock<cSapModel>();
    model.SetupGet(x => x.FrameObj).Returns(frames.Object);
    var store = new ConverterSettingsStore<CsiConversionSettings>();
    store.Initialize(new CsiConversionSettings(model.Object, "mm"));
    var outline = SectionProfileCatalog.TryBuild(new RectangleProfile(DEPTH, WIDTH))!;
    var cache = new CsiToSpeckleCacheSingleton();
    cache.FramePrismCache[SECTION] = new FrameSectionPrism(PrismBuilder.TryBuildUnitPrism(outline), outline, "Rectangular");
    var resolver = new FrameSectionProfileResolver(store, cache, new FrameSectionAreaResolver(store, cache));
    var fallbacks = new ExtrusionFallbackTracker();
    return new Fixture(new VolumetricDisplayValueExtractor(store, resolver, Mock.Of<IShellThicknessResolver>(), fallbacks), fallbacks);
  }
}
