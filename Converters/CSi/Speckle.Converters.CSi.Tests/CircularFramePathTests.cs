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
  private delegate int ReadCurve(string name, ref int type, ref double tension, ref int count, ref double[] x, ref double[] y, ref double[] z);
  private delegate int ReadSection(string name, ref string section, ref string autoSelect);
  private delegate int ReadInsertion(string name, ref int cardinal, ref bool mirror, ref bool stiff, ref double[] start, ref double[] end, ref string system);

  [TestCase(1)]
  [TestCase(-1)]
  public void Extrude_CircularControlsPreserveBothSidesAndEndSections(int side)
  {
    var fixture = CreateFixture([new(-1000, 0, 0), new(1000, 0, 0), new(0, side * 1000, 0)]);
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
    var fixture = CreateFixture([new(-1000, 0, 0), new(1000, 0, 0), new(0, 1000, 0)], type: 2);

    var mesh = fixture.Extractor.TryExtrudeFrame(new CsiFrameWrapper { Name = FRAME }, Axis());

    Assert.That(mesh, Is.Null);
    Assert.That(fixture.Fallbacks.Counts["FRAME/unsupported-curve"], Is.EqualTo(1));
  }

  private static Line Axis() => new()
  {
    start = new Point(-1000, 0, 0, "mm"),
    end = new Point(1000, 0, 0, "mm"),
    units = "mm",
  };

  private static IEnumerable<Vector3> Vertices(Mesh mesh)
  {
    for (int i = 0; i < mesh.vertices.Count; i += 3)
    {
      yield return new Vector3(mesh.vertices[i], mesh.vertices[i + 1], mesh.vertices[i + 2]);
    }
  }

  private sealed record Fixture(VolumetricDisplayValueExtractor Extractor, ExtrusionFallbackTracker Fallbacks);

  private static Fixture CreateFixture(Vector3[] controls, int type = 1, Vector3 offset = default)
  {
    var frames = new Mock<cFrameObj>();
    frames.Setup(x => x.GetSection(FRAME, ref It.Ref<string>.IsAny, ref It.Ref<string>.IsAny))
      .Returns(new ReadSection((string _, ref string section, ref string autoSelect) => { section = SECTION; return 0; }));
    frames.Setup(x => x.GetCurved_2(FRAME, ref It.Ref<int>.IsAny, ref It.Ref<double>.IsAny, ref It.Ref<int>.IsAny, ref It.Ref<double[]>.IsAny, ref It.Ref<double[]>.IsAny, ref It.Ref<double[]>.IsAny))
      .Returns(new ReadCurve((string _, ref int curveType, ref double tension, ref int count, ref double[] x, ref double[] y, ref double[] z) =>
      {
        curveType = type;
        count = controls.Length;
        x = controls.Select(p => p.X).ToArray();
        y = controls.Select(p => p.Y).ToArray();
        z = controls.Select(p => p.Z).ToArray();
        return 0;
      }));
    frames.Setup(x => x.GetInsertionPoint(FRAME, ref It.Ref<int>.IsAny, ref It.Ref<bool>.IsAny, ref It.Ref<bool>.IsAny, ref It.Ref<double[]>.IsAny, ref It.Ref<double[]>.IsAny, ref It.Ref<string>.IsAny))
      .Returns(new ReadInsertion((string _, ref int cardinal, ref bool mirror, ref bool stiff, ref double[] start, ref double[] end, ref string system) =>
      {
        cardinal = 10;
        start = [offset.X, offset.Y, offset.Z];
        end = [offset.X, offset.Y, offset.Z];
        system = "Global";
        return 0;
      }));
    var model = new Mock<cSapModel>();
    model.SetupGet(x => x.FrameObj).Returns(frames.Object);
    var store = new ConverterSettingsStore<CsiConversionSettings>();
    store.Initialize(new CsiConversionSettings(model.Object, "mm"));
    var outline = SectionProfileCatalog.TryBuild(new RectangleProfile(80, 40))!;
    var cache = new CsiToSpeckleCacheSingleton();
    cache.FramePrismCache[SECTION] = new FrameSectionPrism(PrismBuilder.TryBuildUnitPrism(outline), outline, "Rectangular");
    var resolver = new FrameSectionProfileResolver(store, cache, new FrameSectionAreaResolver(store, cache));
    var fallbacks = new ExtrusionFallbackTracker();
    return new Fixture(new VolumetricDisplayValueExtractor(store, resolver, Mock.Of<IShellThicknessResolver>(), fallbacks), fallbacks);
  }
}
