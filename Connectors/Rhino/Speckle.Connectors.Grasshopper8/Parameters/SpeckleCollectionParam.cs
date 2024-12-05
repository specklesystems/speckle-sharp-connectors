using Grasshopper.Kernel;
using Speckle.Connectors.Grasshopper8.HostApp;

namespace Speckle.Connectors.Grasshopper8.Parameters;

public class SpeckleCollectionParam : GH_Param<SpeckleCollectionGoo>
{
  public SpeckleCollectionParam()
    : this(GH_ParamAccess.item) { }

  public SpeckleCollectionParam(IGH_InstanceDescription tag)
    : base(tag) { }

  public SpeckleCollectionParam(IGH_InstanceDescription tag, GH_ParamAccess access)
    : base(tag, access) { }

  public SpeckleCollectionParam(GH_ParamAccess access)
    : base("Speckle Collection", "SpcklCol", "XXX", "Speckle", "Params", access) { }

  public override Guid ComponentGuid => new("F397D941-6B4D-4143-B535-A11F7F776BA1");

  protected override Bitmap Icon => BitmapBuilder.CreateHexagonalBitmap("C");
}
