using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

public partial class SpeckleBlockInstanceWrapperGoo : GH_Goo<SpeckleBlockInstanceWrapper>, IGH_PreviewData
{
  public override bool IsValid => Value?.InstanceProxy != null;
  public override string TypeName => "Speckle Block Instance";
  public override string TypeDescription => "Represents an instance object from Speckle";

  /// <summary>
  /// Creates a default Instance Goo with default values. Only use this for casting.
  /// </summary>
  public SpeckleBlockInstanceWrapperGoo()
  {
    Value = new SpeckleBlockInstanceWrapper(Transform.Identity);
  }

  public SpeckleBlockInstanceWrapperGoo(SpeckleBlockInstanceWrapper value)
  {
    Value = value ?? throw new ArgumentNullException(nameof(value));
  }

  public override IGH_Goo Duplicate() =>
    new SpeckleBlockInstanceWrapperGoo((SpeckleBlockInstanceWrapper)Value.DeepCopy());

  public override string ToString() =>
    $"Speckle Block Instance : {(string.IsNullOrWhiteSpace(Value.Name) ? Value.Base.speckle_type : Value.Name)}";

  public override bool CastFrom(object source)
  {
    switch (source)
    {
      case SpeckleBlockInstanceWrapper sourceWrapper:
        Value = (SpeckleBlockInstanceWrapper)sourceWrapper.DeepCopy();
        return true;
      case SpeckleBlockInstanceWrapperGoo wrapperGoo:
        Value = (SpeckleBlockInstanceWrapper)wrapperGoo.Value.DeepCopy();
        return true;
      case GH_Goo<SpeckleBlockInstanceWrapper> goo:
        Value = (SpeckleBlockInstanceWrapper)goo.Value.DeepCopy();
        return true;
      case GH_Goo<SpeckleObjectWrapper> goo:
        if (goo.Value is SpeckleBlockInstanceWrapper wrapper)
        {
          Value = (SpeckleBlockInstanceWrapper)wrapper.DeepCopy();
          return true;
        }
        break;
    }

    return CastFromModelObject(source);
  }

  public override bool CastTo<T>(ref T target)
  {
    switch (target)
    {
      case Transform:
        target = (T)(object)Value.Transform;
        return true;
      default:
        return CastToModelObject(ref target);
    }
  }

#if !RHINO8_OR_GREATER
  private bool CastFromModelObject(object _) => false;

  private bool CastToModelObject<T>(ref T _) => false;
#endif

  public void DrawViewportWires(GH_PreviewWireArgs args)
  {
    // TODO?
  }

  public void DrawViewportMeshes(GH_PreviewMeshArgs args)
  {
    if (Value?.Definition?.Objects == null)
    {
      return;
    }

    foreach (var obj in Value.Definition.Objects)
    {
      if (obj.GeometryBase != null)
      {
        var transformedGeometry = obj.GeometryBase.Duplicate();
        transformedGeometry.Transform(Value.Transform);
        obj.DrawPreviewRaw(args.Pipeline, args.Material);
      }
    }
  }

  public BoundingBox ClippingBox
  {
    get
    {
      if (Value?.Definition?.Objects == null)
      {
        return new BoundingBox();
      }

      var clippingBox = new BoundingBox();
      foreach (var obj in Value.Definition.Objects)
      {
        if (obj.GeometryBase != null)
        {
          var transformedGeometry = obj.GeometryBase.Duplicate();
          transformedGeometry.Transform(Value.Transform);
          clippingBox.Union(transformedGeometry.GetBoundingBox(false));
        }
      }

      return clippingBox;
    }
  }
}
