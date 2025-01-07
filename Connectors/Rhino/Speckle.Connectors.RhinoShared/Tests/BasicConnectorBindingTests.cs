using FluentAssertions;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Testing;
using Speckle.HostApps;
using Xunit;

namespace Speckle.Connectors.Rhino;

[Collection(RhinoSetup.RhinoCollection)]
public class BasicConnectorBindingTests(IServiceProvider serviceProvider)
{
  [Fact]
  public void Test_Basics()
  {
    var binding = serviceProvider.GetBinding<IBasicConnectorBinding>();
    binding.GetSourceApplicationName().Should().Be("rhino");
    binding.GetDocumentState().Should().BeOfType<TestDocumentModelStore>();
  }
}
