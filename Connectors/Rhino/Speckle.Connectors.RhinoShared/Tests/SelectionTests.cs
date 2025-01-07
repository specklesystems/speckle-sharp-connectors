
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Rhino;
using Speckle.Connectors.DUI.Bindings;
using Xunit;

namespace Speckle.Connectors.Rhino;

[Collection(RhinoSetup.RhinoCollection)]
public class SelectionTests(IServiceProvider serviceProvider)
{
  [Fact]
  public void Test_SelectAll()
  {
    var ids = RhinoDoc.ActiveDoc.Objects.Select(x => x.Id).ToList();    
    RhinoDoc.ActiveDoc.Objects.Select(ids, true);
    var binding = serviceProvider.GetServices<IBinding>().OfType<ISelectionBinding>().Single();
    var selectedObjectIds = binding.GetSelection().SelectedObjectIds;
    
    ids.Should().BeEquivalentTo(selectedObjectIds.Select(Guid.Parse));
  }
}
