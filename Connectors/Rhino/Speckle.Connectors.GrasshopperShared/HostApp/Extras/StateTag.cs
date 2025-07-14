using Grasshopper.Kernel;
using Speckle.Connectors.GrasshopperShared.Properties;

namespace Speckle.Connectors.GrasshopperShared.HostApp.Extras;

public class ListAccessStateTag : GH_StateTag
{
  public override string Description => "This parameter is set to List access";
  public override string Name => "List Access";
  public override Bitmap Icon => Resources.speckle_state_access;
}
