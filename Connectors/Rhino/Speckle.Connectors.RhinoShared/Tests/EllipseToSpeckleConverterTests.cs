using Microsoft.Extensions.DependencyInjection;
using Speckle.Converters.Common.Objects;
using Xunit;

namespace Speckle.Connectors.Rhino;

[Collection(RhinoSetup.RhinoCollection)]
public class EllipseToSpeckleConverterTests(IServiceProvider serviceProvider)
{
  [Fact]
  public void Convert_Test()
  {
    _ = serviceProvider.GetRequiredService<ITypedConverter<RG.Ellipse, SOG.Ellipse>>();
  }
}
