using Grasshopper.Kernel.Types;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

public class SpeckleRootCollectionWrapperGoo : SpeckleCollectionWrapperGoo
{
  public new SpeckleRootCollectionWrapper Value { get; set; }

  public SpeckleRootCollectionWrapperGoo() { }

  public SpeckleRootCollectionWrapperGoo(SpeckleRootCollectionWrapper value)
    : base(value)
  {
    Value = value;
  }

  public override IGH_Goo Duplicate() => new SpeckleRootCollectionWrapperGoo(Value.DeepCopy());

  public override string ToString() => Value?.ToString() ?? "Invalid Root Collection";
}
