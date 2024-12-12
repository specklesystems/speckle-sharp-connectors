using Microsoft.Extensions.DependencyInjection;
using Speckle.Converters.Common.Objects;
using Xunit;

namespace Speckle.Connectors.Rhino;

#pragma warning disable xUnit1041
public class EllipseToSpeckleConverterTests(IServiceProvider serviceProvider)
#pragma warning restore xUnit1041
{
  [Fact]
  public void Convert_Test()
  {
    _ = serviceProvider.GetRequiredService<ITypedConverter<RG.Ellipse, SOG.Ellipse>>();
  }
}
