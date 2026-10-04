using Moq;
using NUnit.Framework;
using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.ToSpeckle.Raw;

namespace Speckle.Converters.CSi.Tests;

public class CsiConversionSettingsFactoryTests
{
  private delegate int ReadUnits(ref eForce force, ref eLength length, ref eTemperature temperature);
  private delegate int ReadPoints(string name, ref string start, ref string end);
  private delegate int ReadCoordinate(string name, ref double x, ref double y, ref double z, string coordinateSystem);

  [TestCase(eLength.mm, 25.4, "mm")]
  [TestCase(eLength.m, 0.0254, "m")]
  [TestCase(eLength.inch, 1, "in")]
  public void Create_SynchronizesApiUnitsBeforeConvertingGeometry(
    eLength lengthUnit,
    double scale,
    string expectedUnits
  )
  {
    var model = new Mock<cSapModel>(MockBehavior.Strict);
    SetupUnits(model, lengthUnit);
    bool synchronized = false;
    model
      .Setup(x => x.SetPresentUnits_2(eForce.kN, lengthUnit, eTemperature.C))
      .Returns(() =>
      {
        synchronized = true;
        return 0;
      });

    var frames = new Mock<cFrameObj>(MockBehavior.Strict);
    frames
      .Setup(x => x.GetPoints("5", ref It.Ref<string>.IsAny, ref It.Ref<string>.IsAny))
      .Returns(
        new ReadPoints(
          (string name, ref string start, ref string end) =>
          {
            start = "51";
            end = "50";
            return 0;
          }
        )
      );
    model.SetupGet(x => x.FrameObj).Returns(frames.Object);

    var points = new Mock<cPointObj>(MockBehavior.Strict);
    points
      .Setup(x =>
        x.GetCoordCartesian(
          It.IsAny<string>(),
          ref It.Ref<double>.IsAny,
          ref It.Ref<double>.IsAny,
          ref It.Ref<double>.IsAny,
          "Global"
        )
      )
      .Returns(
        new ReadCoordinate(
          (string name, ref double x, ref double y, ref double z, string coordinateSystem) =>
          {
            double conversion =
              synchronized ? scale
              : lengthUnit == eLength.m ? 0.001
              : 1;
            x = (name == "51" ? 577.2 : 864) * conversion;
            y = 576 * conversion;
            z = 288 * conversion;
            return 0;
          }
        )
      );
    model.SetupGet(x => x.PointObj).Returns(points.Object);

    var store = new ConverterSettingsStore<CsiConversionSettings>();
    var factory = new CsiConversionSettingsFactory(new CsiToSpeckleUnitConverter(), store);
    store.Initialize(factory.Create(model.Object));
    var line = new LineToSpeckleConverter(store).Convert(new CsiFrameWrapper { Name = "5" });

    Assert.That(line.units, Is.EqualTo(expectedUnits));
    Assert.That(line.end.x - line.start.x, Is.EqualTo(286.8 * scale).Within(1e-8));
    model.Verify(x => x.SetPresentUnits_2(eForce.kN, lengthUnit, eTemperature.C), Times.Once);
  }

  [Test]
  public void Create_StopsWhenReadingUnitsFails()
  {
    var model = new Mock<cSapModel>(MockBehavior.Strict);
    model
      .Setup(x =>
        x.GetPresentUnits_2(ref It.Ref<eForce>.IsAny, ref It.Ref<eLength>.IsAny, ref It.Ref<eTemperature>.IsAny)
      )
      .Returns(1);
    var factory = new CsiConversionSettingsFactory(
      new CsiToSpeckleUnitConverter(),
      new ConverterSettingsStore<CsiConversionSettings>()
    );

    Assert.Throws<InvalidOperationException>(() => factory.Create(model.Object));
    model.Verify(
      x => x.GetPresentUnits_2(ref It.Ref<eForce>.IsAny, ref It.Ref<eLength>.IsAny, ref It.Ref<eTemperature>.IsAny),
      Times.Once
    );
    model.VerifyNoOtherCalls();
  }

  [Test]
  public void Create_StopsWhenSynchronizingUnitsFails()
  {
    var model = new Mock<cSapModel>(MockBehavior.Strict);
    SetupUnits(model, eLength.mm);
    model.Setup(x => x.SetPresentUnits_2(eForce.kN, eLength.mm, eTemperature.C)).Returns(1);
    var factory = new CsiConversionSettingsFactory(
      new CsiToSpeckleUnitConverter(),
      new ConverterSettingsStore<CsiConversionSettings>()
    );

    Assert.Throws<InvalidOperationException>(() => factory.Create(model.Object));
  }

  private static void SetupUnits(Mock<cSapModel> model, eLength lengthUnit) =>
    model
      .Setup(x =>
        x.GetPresentUnits_2(ref It.Ref<eForce>.IsAny, ref It.Ref<eLength>.IsAny, ref It.Ref<eTemperature>.IsAny)
      )
      .Returns(
        new ReadUnits(
          (ref eForce force, ref eLength length, ref eTemperature temperature) =>
          {
            force = eForce.kN;
            length = lengthUnit;
            temperature = eTemperature.C;
            return 0;
          }
        )
      );
}
