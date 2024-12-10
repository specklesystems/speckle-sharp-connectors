using Microsoft.Extensions.DependencyInjection;
using Speckle.Converters.Rhino.ToSpeckle.Raw;
using Xunit;

namespace Speckle.Converters.Rhino7.Tests;

#pragma warning disable xUnit1041
public class EllipseToSpeckleConverterTests(IServiceProvider serviceProvider)
#pragma warning restore xUnit1041
{
  [Fact]
  public void Convert_Test()
  {
    _ = serviceProvider.GetRequiredService<EllipseToSpeckleConverter>();
  }
}
