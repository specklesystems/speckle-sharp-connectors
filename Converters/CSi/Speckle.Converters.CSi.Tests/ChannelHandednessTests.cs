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
  private delegate int ReadChannel(string name, ref string file, ref string material, ref double depth, ref double width,
    ref double flange, ref double web, ref int color, ref string notes, ref string guid);
  private delegate int ReadChannelMirror(string name, ref string file, ref string material, ref double depth, ref double width,
    ref double flange, ref double web, ref bool mirror, ref int color, ref string notes, ref string guid);
  private delegate int ReadArea(string name, ref double area, ref double as2, ref double as3, ref double torsion,
    ref double i22, ref double i33, ref double s22, ref double s33, ref double z22, ref double z33, ref double r22, ref double r33);
  private delegate int ReadSection(string name, ref string section, ref string autoSelect);
  private delegate int ReadAxes(string name, ref double angle, ref bool advanced);
  private delegate int ReadInsertion(string name, ref int cardinal, ref bool mirror, ref bool stiff,
    ref double[] start, ref double[] end, ref string system);

  [TestCase(false, false, false, 0)]
  [TestCase(false, true, false, 0)]
  [TestCase(false, false, false, 90)]
  [TestCase(false, false, true, 0)]
  [TestCase(true, false, false, 0)]
  [TestCase(true, true, false, 0)]
  [TestCase(true, false, false, 90)]
  [TestCase(true, false, true, 0)]
  public void Extrude_C5X9_WebFollowsSourceLocal3(bool mirrored, bool reversed, bool vertical, double angle)
  {
    var fixture = CreateFixture(mirrored, angle);
    var end = vertical ? new Vector3(0, 0, 1000) : new Vector3(reversed ? -1000 : 1000, 0, 0);
    var local2 = vertical ? Vector3.UnitX : angle == 90 ? -Vector3.UnitY : Vector3.UnitZ;
    var local3 = vertical ? Vector3.UnitY : angle == 90 ? -Vector3.UnitZ : reversed ? Vector3.UnitY : -Vector3.UnitY;
    var mesh = fixture.Extractor.TryExtrudeFrame(new CsiFrameWrapper { Name = FRAME }, Axis(end));
    Assert.That(mesh, Is.Not.Null);
    var sectionPoints = ProjectStart(mesh!, Vector3.Zero, local2, local3);
    var webWidths = WebWidths(sectionPoints);
    Assert.That(webWidths, Has.Length.EqualTo(2));
    double expectedMin = mirrored ? -WIDTH / 2 : WIDTH / 2 - WEB;
    double expectedMax = mirrored ? -WIDTH / 2 + WEB : WIDTH / 2;
    TestContext.Out.WriteLine($"C5X9 mirror={mirrored}, reversed={reversed}, vertical={vertical}, angle={angle}: web local3 [{webWidths[0]:F3}, {webWidths[1]:F3}] mm; expected [{expectedMin:F3}, {expectedMax:F3}] mm");
    Assert.That(webWidths[0], Is.EqualTo(expectedMin).Within(1e-6));
    Assert.That(webWidths[1], Is.EqualTo(expectedMax).Within(1e-6));
    Assert.That(sectionPoints.Min(p => p.X), Is.EqualTo(-DEPTH).Within(1e-6));
    Assert.That(sectionPoints.Max(p => p.X), Is.EqualTo(0).Within(1e-6));
    Assert.That(fixture.Fallbacks.Total, Is.Zero);
  }

  private static Line Axis(Vector3 end) => new()
  {
    start = new Point(0, 0, 0, "mm"), end = new Point(end.X, end.Y, end.Z, "mm"), units = "mm"
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
    for (int i = 0; i < points.Count; i++)
    {
      var start = points[i];
      var end = points[(i + 1) % points.Count];
      double slice = -DEPTH / 2;
      if ((start.X < slice && end.X > slice) || (start.X > slice && end.X < slice))
      {
        widths.Add(start.Y + (end.Y - start.Y) * (slice - start.X) / (end.X - start.X));
      }
    }
    return widths.Order().ToArray();
  }

  private sealed record Fixture(VolumetricDisplayValueExtractor Extractor, FrameSectionProfileResolver Resolver,
    ExtrusionFallbackTracker Fallbacks, Mock<cPropFrame> Properties);

  private static Fixture CreateFixture(bool mirrored, double angle = 0)
  {
    var properties = new Mock<cPropFrame>();
    properties.Setup(x => x.GetTypeOAPI(SECTION, ref It.Ref<eFramePropType>.IsAny))
      .Returns(new ReadType((string _, ref eFramePropType type) => { type = eFramePropType.Channel; return 0; }));
    properties.Setup(x => x.GetChannel(SECTION, ref It.Ref<string>.IsAny, ref It.Ref<string>.IsAny,
      ref It.Ref<double>.IsAny, ref It.Ref<double>.IsAny, ref It.Ref<double>.IsAny, ref It.Ref<double>.IsAny,
      ref It.Ref<int>.IsAny, ref It.Ref<string>.IsAny, ref It.Ref<string>.IsAny))
      .Returns(new ReadChannel((string _, ref string file, ref string material, ref double depth, ref double width,
        ref double flange, ref double web, ref int color, ref string notes, ref string guid) =>
      { depth = DEPTH; width = WIDTH; flange = FLANGE; web = WEB; return 0; }));
    properties.Setup(x => x.GetChannel_1(SECTION, ref It.Ref<string>.IsAny, ref It.Ref<string>.IsAny,
      ref It.Ref<double>.IsAny, ref It.Ref<double>.IsAny, ref It.Ref<double>.IsAny, ref It.Ref<double>.IsAny,
      ref It.Ref<bool>.IsAny, ref It.Ref<int>.IsAny, ref It.Ref<string>.IsAny, ref It.Ref<string>.IsAny))
      .Returns(new ReadChannelMirror((string _, ref string file, ref string material, ref double depth, ref double width,
        ref double flange, ref double web, ref bool mirror, ref int color, ref string notes, ref string guid) =>
      { depth = DEPTH; width = WIDTH; flange = FLANGE; web = WEB; mirror = mirrored; return 0; }));
    properties.Setup(x => x.GetSectProps(SECTION, ref It.Ref<double>.IsAny, ref It.Ref<double>.IsAny,
      ref It.Ref<double>.IsAny, ref It.Ref<double>.IsAny, ref It.Ref<double>.IsAny, ref It.Ref<double>.IsAny,
      ref It.Ref<double>.IsAny, ref It.Ref<double>.IsAny, ref It.Ref<double>.IsAny, ref It.Ref<double>.IsAny,
      ref It.Ref<double>.IsAny, ref It.Ref<double>.IsAny))
      .Returns(new ReadArea((string _, ref double area, ref double as2, ref double as3, ref double torsion,
        ref double i22, ref double i33, ref double s22, ref double s33, ref double z22, ref double z33, ref double r22, ref double r33) =>
      { area = 1703.2; return 0; }));
    var frames = new Mock<cFrameObj>();
    frames.Setup(x => x.GetSection(FRAME, ref It.Ref<string>.IsAny, ref It.Ref<string>.IsAny))
      .Returns(new ReadSection((string _, ref string section, ref string autoSelect) => { section = SECTION; return 0; }));
    frames.Setup(x => x.GetLocalAxes(FRAME, ref It.Ref<double>.IsAny, ref It.Ref<bool>.IsAny))
      .Returns(new ReadAxes((string _, ref double result, ref bool advanced) => { result = angle; return 0; }));
    frames.Setup(x => x.GetInsertionPoint(FRAME, ref It.Ref<int>.IsAny, ref It.Ref<bool>.IsAny,
      ref It.Ref<bool>.IsAny, ref It.Ref<double[]>.IsAny, ref It.Ref<double[]>.IsAny, ref It.Ref<string>.IsAny))
      .Returns(new ReadInsertion((string _, ref int cardinal, ref bool mirror, ref bool stiff,
        ref double[] start, ref double[] end, ref string system) => { cardinal = 8; return 0; }));
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
