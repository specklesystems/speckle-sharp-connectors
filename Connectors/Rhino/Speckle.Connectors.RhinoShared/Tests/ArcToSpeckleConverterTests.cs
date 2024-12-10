using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Converters.Rhino.ToSpeckle.Raw;
using Xunit;

namespace Speckle.Converters.Rhino7.Tests;

#pragma warning disable xUnit1041
public class ArcToSpeckleConverterTests(IServiceProvider serviceProvider)
#pragma warning restore xUnit1041
{
  
  [Fact]
  public void Convert_ShouldConvertArcCorrectly()
  {
    // Arrange
    var converter = serviceProvider.GetRequiredService<ArcToSpeckleConverter>();

    var arc = new RG.Arc(new RG.Point3d(), new RG.Point3d(), new RG.Point3d()) { Plane = new RG.Plane() };

    // Act
    var result = converter.Convert(arc);

    // Assert
    result.Should().NotBeNull();
  }
}
