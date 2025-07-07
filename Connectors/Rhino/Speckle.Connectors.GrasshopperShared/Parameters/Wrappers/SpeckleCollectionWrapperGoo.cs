using Grasshopper.Kernel.Types;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

public partial class SpeckleCollectionWrapperGoo : GH_Goo<SpeckleCollectionWrapper> //, IGH_PreviewData // can be made previewable later
{
  public override bool IsValid => Value.Collection is not null;
  public override string TypeName => "Speckle Collection";
  public override string TypeDescription => "Represents a collection from Speckle";

  public SpeckleCollectionWrapperGoo() { }

  public SpeckleCollectionWrapperGoo(SpeckleCollectionWrapper value)
  {
    Value = value;
  }

  public override IGH_Goo Duplicate() => new SpeckleCollectionWrapperGoo(Value.DeepCopy());

  public override string ToString() => $"Speckle Collection : {Value.Name} ({Value.Elements.Count})";

  public override bool CastFrom(object source)
  {
    switch (source)
    {
      case SpeckleCollectionWrapper sourceWrapper:
        Value = sourceWrapper;
        return true;
      case SpeckleCollectionWrapperGoo wrapperGoo:
        Value = wrapperGoo.Value;
        return true;
    }

    // Handle case of model objects in rhino 8
    return CastFromModelLayer(source);
  }

#if !RHINO8_OR_GREATER
  private bool CastFromModelLayer(object _) => false;

  private bool CastToModelLayer<T>(ref T _) => false;
#endif

  public override bool CastTo<T>(ref T target)
  {
    return CastToModelLayer(ref target);
  }
}
