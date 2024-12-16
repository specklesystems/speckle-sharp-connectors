using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Converters.Common.Objects;
using Xunit;

namespace Speckle.Connectors.Rhino;


[Collection(RhinoSetup.RhinoCollection)]
public class ArcToSpeckleConverterTests(IServiceProvider serviceProvider)
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
