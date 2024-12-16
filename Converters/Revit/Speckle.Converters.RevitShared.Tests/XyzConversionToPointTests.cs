using FluentAssertions;
using Speckle.Connectors.Rhino;
using Speckle.Converters.RevitShared;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.ToSpeckle;
using Speckle.HostApps;
using Xunit;

namespace Speckle.Converters.Revit2023.Tests;

[Collection(RevitSetup.RevitCollection)]
public class XyzConversionToPointTests(IServiceProvider serviceProvider) : MoqTest
{
  [Fact]
  public void Convert_Point()
  {
    var x = 3.1;
    var y = 3.2;
    var z = 3.3;
    var xScaled = 4.1;
    var yScaled = 4.2;
    var zScaled = 4.3;

    var xyz2 = new DB.XYZ(x, y, z);
    var xyz1 = new DB.XYZ(0, 1, 0);

    var referencePointConverter = Create<IReferencePointConverter>();
    referencePointConverter.Setup(x => x.ConvertToExternalCoordinates(xyz1, true)).Returns(xyz2);

    var scalingServiceToSpeckle = Create<IScalingServiceToSpeckle>();
    scalingServiceToSpeckle.Setup(a => a.ScaleLength(x)).Returns(xScaled);
    scalingServiceToSpeckle.Setup(a => a.ScaleLength(y)).Returns(yScaled);
    scalingServiceToSpeckle.Setup(a => a.ScaleLength(z)).Returns(zScaled);

    var converter = serviceProvider.Create<XyzConversionToPoint>(
      referencePointConverter.Object,
      scalingServiceToSpeckle.Object
    );
    var point = converter.Convert(xyz1);

    point.x.Should().Be(xScaled);
    point.y.Should().Be(yScaled);
    point.z.Should().Be(zScaled);
    point.units.Should().Be(RevitSetup.DEFAULT_UNITS);
  }
}
