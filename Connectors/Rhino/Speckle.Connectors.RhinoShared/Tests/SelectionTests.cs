using FluentAssertions;
using Rhino;
using Speckle.Connectors.DUI.Bindings;
using Speckle.HostApps;
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
    var binding = serviceProvider.GetBinding<ISelectionBinding>();
    var selectedObjectIds = binding.GetSelection().SelectedObjectIds;
    
    ids.Should().BeEquivalentTo(selectedObjectIds.Select(Guid.Parse));
  }
  
  
  [Fact]
  public async Task Test_SelectAll_ViaBasicBinding()
  {
    var ids = RhinoDoc.ActiveDoc.Objects.Select(x => x.Id.ToString()).ToList();
    

    await serviceProvider.GetBinding<IBasicConnectorBinding>().HighlightObjects(ids);
    var binding = serviceProvider.GetBinding<ISelectionBinding>();
    var selectedObjectIds = binding.GetSelection().SelectedObjectIds;
    
    ids.Should().BeEquivalentTo(selectedObjectIds.Select(Guid.Parse));
  }
}
