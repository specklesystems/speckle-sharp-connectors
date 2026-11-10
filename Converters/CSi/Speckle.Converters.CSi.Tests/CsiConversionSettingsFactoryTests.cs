using System.Linq.Expressions;
using Moq;
using NUnit.Framework;
using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.ToSpeckle.Raw;
using Speckle.Testing;

namespace Speckle.Converters.CSi.Tests;

public class CsiConversionSettingsFactoryTests : MoqTest
{
  private const string FRAME_NAME = "5";
  private const string START_JOINT_NAME = "51";
  private const string END_JOINT_NAME = "50";
  private const double START_X_INCHES = 577.2;
  private const double END_X_INCHES = 864;
  private const double SPAN_INCHES = END_X_INCHES - START_X_INCHES;

  private static readonly Expression<Func<cSapModel, int>> s_readPresentUnits = x =>
    x.GetPresentUnits_2(ref It.Ref<eForce>.IsAny, ref It.Ref<eLength>.IsAny, ref It.Ref<eTemperature>.IsAny);

  private delegate int ReadUnits(ref eForce force, ref eLength length, ref eTemperature temperature);
  private delegate int ReadPoints(string name, ref string start, ref string end);
  private delegate int ReadCoordinate(string name, ref double x, ref double y, ref double z, string coordinateSystem);

  [TestCase(eLength.mm, 25.4, "mm")]
  [TestCase(eLength.m, 0.0254, "m")]
  [TestCase(eLength.inch, 1, "in")]
  public void Create_SynchronizesApiUnitsBeforeConvertingGeometry(
    eLength presentLengthUnit,
    double inchesToPresentUnits,
    string expectedUnits
  )
  {
    var model = Create<cSapModel>();
    SetupPresentUnits(model, presentLengthUnit);
    bool synchronized = false;
    model
      .Setup(x => x.SetPresentUnits_2(eForce.kN, presentLengthUnit, eTemperature.C))
      .Returns(() =>
      {
        synchronized = true;
        return 0;
      });

    var frames = Create<cFrameObj>();
    frames
      .Setup(x => x.GetPoints(FRAME_NAME, ref It.Ref<string>.IsAny, ref It.Ref<string>.IsAny))
      .Returns(
        new ReadPoints(
          (string _, ref string start, ref string end) =>
          {
            start = START_JOINT_NAME;
            end = END_JOINT_NAME;
            return 0;
          }
        )
      );
    model.SetupGet(x => x.FrameObj).Returns(frames.Object);

    var points = Create<cPointObj>();
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
          (string name, ref double x, ref double y, ref double z, string _) =>
          {
            // ENG-10421: the model is stored in inches, and the API hands back those numbers until synchronized.
            double apiConversionFactor = synchronized ? inchesToPresentUnits : 1;
            x = (name == START_JOINT_NAME ? START_X_INCHES : END_X_INCHES) * apiConversionFactor;
            y = 576 * apiConversionFactor;
            z = 288 * apiConversionFactor;
            return 0;
          }
        )
      );
    model.SetupGet(x => x.PointObj).Returns(points.Object);

    var store = new ConverterSettingsStore<CsiConversionSettings>();
    store.Initialize(CreateFactory(store).Create(model.Object));
    var line = new LineToSpeckleConverter(store).Convert(new CsiFrameWrapper { Name = FRAME_NAME });

    Assert.That(line.units, Is.EqualTo(expectedUnits));
    Assert.That(line.end.x - line.start.x, Is.EqualTo(SPAN_INCHES * inchesToPresentUnits).Within(1e-8));
    model.Verify(x => x.SetPresentUnits_2(eForce.kN, presentLengthUnit, eTemperature.C), Times.Once);
  }

  [Test]
  public void Create_StopsWhenReadingUnitsFails()
  {
    var model = Create<cSapModel>();
    model.Setup(s_readPresentUnits).Returns(1);

    Assert.Throws<InvalidOperationException>(() => CreateFactory().Create(model.Object));
    model.Verify(s_readPresentUnits, Times.Once);
  }

  [Test]
  public void Create_StopsWhenSynchronizingUnitsFails()
  {
    var model = Create<cSapModel>();
    SetupPresentUnits(model, eLength.mm);
    model.Setup(x => x.SetPresentUnits_2(eForce.kN, eLength.mm, eTemperature.C)).Returns(1);

    Assert.Throws<InvalidOperationException>(() => CreateFactory().Create(model.Object));
  }

  private static CsiConversionSettingsFactory CreateFactory(
    ConverterSettingsStore<CsiConversionSettings>? store = null
  ) => new(new CsiToSpeckleUnitConverter(), store ?? new ConverterSettingsStore<CsiConversionSettings>());

  private static void SetupPresentUnits(Mock<cSapModel> model, eLength presentLengthUnit) =>
    model
      .Setup(s_readPresentUnits)
      .Returns(
        new ReadUnits(
          (ref eForce force, ref eLength length, ref eTemperature temperature) =>
          {
            force = eForce.kN;
            length = presentLengthUnit;
            temperature = eTemperature.C;
            return 0;
          }
        )
      );
}
