using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Speckle.Connectors.Grasshopper8.HostApp;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.Grasshopper8.Parameters;

// public class SpeckleCollectionWrapper : Base
// {
//   public Collection OriginalObject { get; set; }
//
//   public override string ToString() => $"{OriginalObject.name} [{OriginalObject.elements.Count}]";
// }

public class SpeckleCollectionGoo : GH_Goo<Collection> //, IGH_PreviewData // can be made previewable later
{
  public override IGH_Goo Duplicate() => throw new NotImplementedException();

  public override string ToString() => $"{Value.name} ({Value.elements.Count})";

  public override bool IsValid => true;
  public override string TypeName => "Speckle collection wrapper";
  public override string TypeDescription => "Speckle collection wrapper";

  public SpeckleCollectionGoo() { }

  public SpeckleCollectionGoo(Collection value)
  {
    Value = value;
  }
}

public class SpeckleCollectionWrapperParam : GH_Param<SpeckleCollectionGoo>
{
  public SpeckleCollectionWrapperParam()
    : this(GH_ParamAccess.item) { }

  public SpeckleCollectionWrapperParam(IGH_InstanceDescription tag)
    : base(tag) { }

  public SpeckleCollectionWrapperParam(IGH_InstanceDescription tag, GH_ParamAccess access)
    : base(tag, access) { }

  public SpeckleCollectionWrapperParam(GH_ParamAccess access)
    : base("Speckle Collection Wrapper", "SCO", "XXXXX", "Speckle", "Params", access) { }

  public override Guid ComponentGuid => new("6E871D5B-B221-4992-882A-EFE6796F3010");
  protected override Bitmap Icon => BitmapBuilder.CreateHexagonalBitmap("C");
}
