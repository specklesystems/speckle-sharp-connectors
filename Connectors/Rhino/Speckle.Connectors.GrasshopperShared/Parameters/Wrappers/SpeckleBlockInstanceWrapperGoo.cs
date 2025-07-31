using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Registration;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

public partial class SpeckleBlockInstanceWrapperGoo : GH_Goo<SpeckleBlockInstanceWrapper>, IGH_PreviewData
{
  public override bool IsValid => Value?.InstanceProxy != null && Value.ApplicationId is not null;
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

  //POC: we probably shouldn't be deep copying here!!! do so in each component that mutates inputs...
  public override bool CastFrom(object source)
  {
    switch (source)
    {
      case SpeckleBlockInstanceWrapper sourceWrapper:
        Value = sourceWrapper;
        return true;
      case SpeckleBlockInstanceWrapperGoo wrapperGoo:
        Value = wrapperGoo.Value;
        return true;
      case GH_Goo<SpeckleBlockInstanceWrapper> goo:
        Value = goo.Value;
        return true;
      case SpeckleGeometryWrapperGoo objWrapperGoo:
        if (objWrapperGoo.Value is SpeckleBlockInstanceWrapper objWrapper)
        {
          Value = objWrapper;
          return true;
        }
        break;
      case GH_Goo<SpeckleGeometryWrapper> goo:
        if (goo.Value is SpeckleBlockInstanceWrapper wrapper)
        {
          Value = wrapper;
          return true;
        }
        break;
      case IGH_GeometricGoo geometricGoo:
        // this happens when you assign instances in rhino to a model isntance param
        // need to get the id of the referenced geometry here and pass the retrieved object
        if (geometricGoo.IsReferencedGeometry)
        {
          return CurrentDocument.Document?.Objects.FindId(geometricGoo.ReferenceID) is RhinoObject rhinoObj
                 && CastFromModelObject(rhinoObj);
        }

        if (geometricGoo is not InstanceReferenceGeometry instance)
        {
          return false;
        }

        Base converted = SpeckleConversionContext.Current.ConvertToSpeckle(instance);
        Value = new SpeckleBlockInstanceWrapper()
        {
          GeometryBase = instance,
          Base = converted,
          Transform = instance.Xform,
          ApplicationId = Guid.NewGuid().ToString(),
        };
        return true;
    }

    return CastFromModelObject(source);
  }

  public override bool CastTo<T>(ref T target)
  {
    switch (target)
    {
      case SpeckleGeometryWrapperGoo:
        target = (T)(object)Value;
        return true;
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

  public void DrawViewportMeshes(GH_PreviewMeshArgs args) => Value?.DrawPreviewRaw(args.Pipeline, args.Material);

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
