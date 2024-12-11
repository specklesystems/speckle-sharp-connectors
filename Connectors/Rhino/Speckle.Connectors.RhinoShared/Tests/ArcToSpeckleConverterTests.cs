using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Rhino;
using Speckle.Connectors.Rhino.DependencyInjection;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Rhino;
using Speckle.Converters.Rhino7.Tests;
using Xunit;

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
