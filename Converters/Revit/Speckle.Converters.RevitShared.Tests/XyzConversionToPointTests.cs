using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;
using Speckle.HostApps;
using Xunit;

namespace Speckle.Converters.Revit2023.Tests;


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

    var xyz = new DB.XYZ(x,y,z);


    var units = "units";
    var conversionContext = Create<IConverterSettingsStore<RevitConversionSettings>>();
    conversionContext.Setup(x => x.Current).Returns(new RevitConversionSettings(null!, DetailLevelType.Coarse, null, units, false) );

    var scalingServiceToSpeckle = Create<IScalingServiceToSpeckle>();
    scalingServiceToSpeckle.Setup(a => a.ScaleLength(x)).Returns(xScaled);
    scalingServiceToSpeckle.Setup(a => a.ScaleLength(y)).Returns(yScaled);
    scalingServiceToSpeckle.Setup(a => a.ScaleLength(z)).Returns(zScaled);

    var converter = ActivatorUtilities.CreateInstance<ITypedConverter<DB.XYZ, SOG.Point>>(serviceProvider);
    var point = converter.Convert(xyz);

    point.x.Should().Be(xScaled);
    point.y.Should().Be(yScaled);
    point.z.Should().Be(zScaled);
    point.units.Should().Be(units);
  }

  
  }
