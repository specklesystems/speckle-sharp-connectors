using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Rhino;
using Speckle.Connectors.Rhino.DependencyInjection;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Rhino;
using Speckle.Converters.Rhino7.Tests;
using Xunit;

[assembly: TestFramework($"Speckle.Converters.Rhino7.Tests.{nameof(SpeckleXunitTestFramework)}", "Speckle.Connectors.Rhino8")]
namespace Speckle.Converters.Rhino7.Tests;

public class RhinoTest
{
  public RhinoTest(IServiceProvider serviceProvider)
  {
    serviceProvider.GetRequiredService<IConverterSettingsStore<RhinoConversionSettings>>()
      .Initialize(serviceProvider.GetRequiredService<IRhinoConversionSettingsFactory>().Create(RhinoDoc.ActiveDoc));

  }
}

#pragma warning disable xUnit1041

[Collection(nameof(RhinoPlugin))]
#pragma warning disable CS9107 // Parameter is captured into the state of the enclosing type and its value is also passed to the base constructor. The value might be captured by the base class as well.
public class ArcToSpeckleConverterTests(IServiceProvider serviceProvider) : RhinoTest(serviceProvider)
#pragma warning restore CS9107 // Parameter is captured into the state of the enclosing type and its value is also passed to the base constructor. The value might be captured by the base class as well.
#pragma warning restore xUnit1041
{
  
  [Fact]
  public void Convert_ShouldConvertArcCorrectly()
  {
    // Arrange
    var converter = serviceProvider.GetRequiredService<ITypedConverter<RG.Arc, SOG.Arc>>();

    var arc = new RG.Arc(new RG.Point3d(), new RG.Point3d(), new RG.Point3d()) { Plane = new RG.Plane() };

    // Act
    var result = converter.Convert(arc);

    // Assert
    result.Should().NotBeNull();
  }
}
