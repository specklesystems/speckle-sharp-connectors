using FluentAssertions;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Raw;
using Speckle.Converters.RevitShared.Settings;
using Speckle.HostApps;
using Speckle.Sdk.Common.Exceptions;
using Xunit;

namespace Speckle.Converters.Revit2023.Tests;

public class ModelCurveArrayToSpeckleConverterTests(IServiceProvider serviceProvider) : MoqTest
{
  [Fact]
  public void Convert_Empty()
  {
    var conversionContext = CreateSettingsStore<RevitConversionSettings>();
    var scalingService = this.CreateScalingService();
    var converter = Create<ITypedConverter<DB.Curve, SO.ICurve>>();

    var sut = serviceProvider.Create<ModelCurveArrayToSpeckleConverter>(
      conversionContext.Object,
      scalingService.Object,
      converter.Object
    );
    Assert.Throws<ValidationException>(() => sut.Convert(new DB.ModelCurveArray()));
  }

  [Fact]
  public void Convert()
  {
    var conversionContext = CreateSettingsStore<RevitConversionSettings>();
    var scalingService = this.CreateScalingService();
    var curveConverter = Create<ITypedConverter<DB.Curve, SO.ICurve>>();

    var endpoint1 = Create<DB.XYZ>();
    var geometry1 = Create<DB.Curve>();
    var curve1 = Create<DB.ModelCurve>();
    curve1.Setup(x => x.GeometryCurve).Returns(geometry1.Object);
    geometry1.Setup(x => x.Length).Returns(2);
    geometry1.Setup(x => x.GetEndPoint(0)).Returns(endpoint1.Object);

    var endpoint2 = Create<DB.XYZ>();
    var geometry2 = Create<DB.Curve>();
    var curve2 = Create<DB.ModelCurve>();
    curve2.Setup(x => x.GeometryCurve).Returns(geometry2.Object);
    geometry2.Setup(x => x.Length).Returns(3);
    geometry2.Setup(x => x.GetEndPoint(1)).Returns(endpoint2.Object);

    var units = "units";
    conversionContext
      .Setup(x => x.Current)
      .Returns(new RevitConversionSettings(null!, DetailLevelType.Coarse, null, units, false));

    var scaleLength = 2.2;
    scalingService.Setup(x => x.ScaleLength(2 + 3)).Returns(scaleLength);

    endpoint1.Setup(x => x.DistanceTo(endpoint2.Object)).Returns(4.4);

    curveConverter.Setup(x => x.Convert(geometry1.Object)).Returns(Create<SO.ICurve>().Object);
    curveConverter.Setup(x => x.Convert(geometry2.Object)).Returns(Create<SO.ICurve>().Object);

    var sut = serviceProvider.Create<ModelCurveArrayToSpeckleConverter>(
      conversionContext.Object,
      scalingService.Object,
      curveConverter.Object
    );
    var array = Create<DB.ModelCurveArray>();

    array
      .Setup(x => x.GetEnumerator())
      .Returns(new List<DB.ModelCurve> { curve1.Object, curve2.Object }.GetEnumerator());
    var polycurve = sut.Convert(array.Object);

    polycurve.units.Should().Be(units);
    polycurve.closed.Should().BeFalse();
    polycurve.length.Should().Be(scaleLength);
    polycurve.segments.Count.Should().Be(2);
  }
}
