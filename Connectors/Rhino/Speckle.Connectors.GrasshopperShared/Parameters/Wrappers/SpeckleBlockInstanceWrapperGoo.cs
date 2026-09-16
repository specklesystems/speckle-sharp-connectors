using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Connectors.GrasshopperShared.HostApp;
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
          return RhinoDoc.ActiveDoc?.Objects.FindId(geometricGoo.ReferenceID) is RhinoObject rhinoObj
            && CastFromModelObject(rhinoObj);
        }

        if (geometricGoo is not InstanceReferenceGeometry instance)
        {
          return false;
        }

        Base? converted = SpeckleConversionContext.Current.ConvertToSpeckle(instance);

        if (converted is null)
        {
          return false; // gh deals with false return from casting as warning 😎
        }

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
        if (CastToModelObject(ref target))
        {
          return true;
        }

        // Instances have no native geometry of their own (their GeometryBase is a placeholder
        // InstanceReferenceGeometry - see GrasshopperBlockUnpacker). Standard downstream components
        // (Mesh, Brep, Curve, ...) can't consume that, so wiring an instance straight into one used to
        // silently produce nothing (FEA-694). Fall back to the single defining object's geometry,
        // transformed into world space, and let SpeckleGeometryWrapperGoo's existing GH_Convert-based
        // casts take it from there. Ambiguous for multi-object or nested-instance definitions - those
        // still need to go through the Speckle Block Instance / Block Definition components.
        return TryGetSingleTransformedGeometryGoo() is SpeckleGeometryWrapperGoo geometryGoo
          && geometryGoo.CastTo(ref target);
    }
  }

  /// <summary>
  /// Resolves this instance to its single defining geometry object, transformed into world space.
  /// </summary>
  /// <returns>
  /// Null when the definition is missing or empty, has more than one object, or its sole object is
  /// itself a nested instance or has no geometry - all ambiguous cases where the caller should use the
  /// Speckle Block Instance / Block Definition components to deconstruct explicitly instead.
  /// </returns>
  private SpeckleGeometryWrapperGoo? TryGetSingleTransformedGeometryGoo()
  {
    if (Value?.Definition?.Objects is not { Count: 1 } objects || objects[0] is SpeckleBlockInstanceWrapper)
    {
      return null;
    }

    SpeckleGeometryWrapper singleObject = objects[0];
    if (singleObject.GeometryBase == null)
    {
      return null;
    }

    SpeckleGeometryWrapper transformed = singleObject.DeepCopy();
    transformed.GeometryBase!.Transform(Value.Transform);
    return new SpeckleGeometryWrapperGoo(transformed);
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
