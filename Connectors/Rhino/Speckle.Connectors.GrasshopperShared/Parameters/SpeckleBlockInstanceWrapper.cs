using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Connectors.GrasshopperShared.Components;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Properties;
using Speckle.DoubleNumerics;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Instances;
using Plane = Rhino.Geometry.Plane;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

/// <summary>
/// A Wrapper class representing a block instance.
/// </summary>
public class SpeckleBlockInstanceWrapper : SpeckleWrapper
{
  public InstanceProxy InstanceProxy { get; set; } // NOTE: stores the actual typed object from `Base`
  public override required Base Base // NOTE: `InstanceProxy` wraps `Base` just like `SpeckleCollectionWrapper` and `SpeckleMaterialWrapper`
  {
    get => InstanceProxy;
    set
    {
      if (value is not InstanceProxy proxy)
      {
        throw new ArgumentException("Cannot create block instance wrapper from a non-InstanceProxy Base");
      }

      InstanceProxy = proxy;
      UpdateTransformFromProxy();
    }
  }

  // TODO: Add when SpeckleBlockDefinitionWrapper is available (blocked by [CNX-1941](https://linear.app/speckle/issue/CNX-1941/add-speckle-blockdefinition-param))
  // public SpeckleBlockDefinitionWrapper? Definition { get; set; }

  public override string ToString() => $"Speckle Block Instance [{Name}]";

  public SpecklePropertyGroupGoo Properties { get; set; } = new();

  // TODO: we need to wait on this. not sure how to tackle this 🤯 overrides etc.
  /*public Color? Color { get; set; }
  public SpeckleMaterialWrapper? Material { get; set; }*/

  private Transform _transform = Transform.Identity;
  public Transform Transform
  {
    get => _transform;
    set
    {
      _transform = value;
      UpdateProxyFromTransform();
    }
  }

  /// <summary>
  /// Updates Rhino Transform property when the InstanceProxy.transform changes.
  /// </summary>
  /// <remarks>
  /// This happens when we receive data from Speckle - we need to convert the Speckle Matrix4x4
  /// back to a Rhino Transform so Grasshopper users can work with familiar Rhino geometry.
  /// </remarks>
  private void UpdateTransformFromProxy()
  {
    if (InstanceProxy?.transform != null)
    {
      var units = InstanceProxy.units;
      _transform = GrasshopperHelpers.MatrixToTransform(InstanceProxy.transform, units);
    }
  }

  /// <summary>
  /// Updates the InstanceProxy.transform when the Rhino Transform property changes.
  /// </summary>
  /// <remarks>
  /// This happens when users input a transform in Grasshopper - we need to convert it
  /// to Speckle's Matrix4x4 format so it can be sent to Speckle properly.
  /// Uses the document's unit system to ensure proper scaling between different units.
  /// </remarks>
  private void UpdateProxyFromTransform()
  {
    if (InstanceProxy != null)
    {
      // TODO: TransformToMatrix method in [feat(grasshopper): add Speckle Block Definition support #891](https://github.com/specklesystems/speckle-sharp-connectors/pull/891/files)
      // var units = InstanceProxy.units;
      // InstanceProxy.transform = GrasshopperHelpers.TransformToMatrix(_transform, units);
    }
  }

  public void DrawPreview(IGH_PreviewArgs args, bool isSelected = false)
  {
    // TODO: preview by transforming definitions geo
    // need access to block definitions geometry
    // add when SpeckleBlockDefinitionWrapper is available (blocked by [CNX-1941](https://linear.app/speckle/issue/CNX-1941/add-speckle-blockdefinition-param))
    throw new NotImplementedException();
  }

  public void Bake(RhinoDoc doc, List<Guid> blockIds, int bakeLayerIndex = -1, bool layersAlreadyCreated = false)
  {
    // TODO: create InstanceReference in Rhino doc
    // Will need the definition index and transform
    // add when SpeckleBlockDefinitionWrapper is available (blocked by [CNX-1941](https://linear.app/speckle/issue/CNX-1941/add-speckle-blockdefinition-param))
    throw new NotImplementedException();
  }

  public SpeckleBlockInstanceWrapper DeepCopy() =>
    new()
    {
      Base = InstanceProxy.ShallowCopy(),
      Transform = _transform,
      Properties = Properties,
      ApplicationId = ApplicationId,
      Name = Name
    };
}

public class SpeckleBlockInstanceWrapperGoo : GH_Goo<SpeckleBlockInstanceWrapper>, IGH_PreviewData, ISpeckleGoo
{
  public override string ToString() => $@"Speckle Block Instance Goo [{m_value.Base.speckle_type}]";

  public override bool IsValid => true;
  public override string TypeName => "Speckle block instance wrapper";
  public override string TypeDescription => "A wrapper around speckle grasshopper block instances.";

  public override bool CastFrom(object source)
  {
    switch (source)
    {
      case SpeckleBlockInstanceWrapper wrapper:
        Value = wrapper;
        return true;

      case GH_Goo<SpeckleBlockInstanceWrapper> wrapperGoo:
        Value = wrapperGoo.Value;
        return true;

      case Transform transform:
        return CreateFromTransform(transform);

      case Plane plane:
        var planeTransform = Transform.PlaneToPlane(Plane.WorldXY, plane);
        return CreateFromTransform(planeTransform);

      case InstanceReferenceGeometry instanceRef:
        return CreateFromInstanceReference(instanceRef);
    }
    return false;
  }

  public override bool CastTo<T>(ref T target)
  {
    if (Value == null)
    {
      return false;
    }

    var type = typeof(T);

    if (type == typeof(Transform))
    {
      target = (T)(object)Value.Transform;
      return true;
    }

    if (type == typeof(InstanceProxy))
    {
      target = (T)(object)Value.InstanceProxy;
      return true;
    }

    if (type == typeof(InstanceReferenceGeometry))
    {
      return CreateInstanceReferenceGeometry(ref target);
    }

    return false;
  }

  private bool CreateFromTransform(Transform transform)
  {
    var units = RhinoDoc.ActiveDoc?.ModelUnitSystem.ToSpeckleString() ?? "none";
    Value = new SpeckleBlockInstanceWrapper()
    {
      Base = new InstanceProxy()
      {
        definitionId = "placeholder-definition-id", // TODO: Set from actual block definition
        // definitionId = blockDefinition.ApplicationId TODO: Set from actual block definition
        maxDepth = 1, // Standard depth for single instance
        transform = TransformToMatrix(transform), // TODO: Remove when GrasshopperHelpers.TransformToMatrix is available from merged PR
        //transform = GrasshopperHelpers.TransformToMatrix(transform, units),
        units = units,
        applicationId = Guid.NewGuid().ToString()
      },
      Transform = transform,
      ApplicationId = Guid.NewGuid().ToString()
    };
    return true;
  }

  private bool CreateFromInstanceReference(InstanceReferenceGeometry instanceRef)
  {
    var units = RhinoDoc.ActiveDoc?.ModelUnitSystem.ToSpeckleString() ?? "none";
    var definitionId = instanceRef.ParentIdefId.ToString();

    Value = new SpeckleBlockInstanceWrapper()
    {
      Base = new InstanceProxy()
      {
        definitionId = definitionId,
        maxDepth = 1,
        transform = TransformToMatrix(instanceRef.Xform), // TODO: Use helper when available
        //transform = TransformToMatrix(instanceRef.Xform, units), // TODO: Use helper when available
        units = units,
        applicationId = Guid.NewGuid().ToString()
      },
      Transform = instanceRef.Xform,
      ApplicationId = Guid.NewGuid().ToString()
    };
    return true;
  }

  //private bool CreateInstanceReferenceGeometry<T>(ref T target)
  private bool CreateInstanceReferenceGeometry<T>(ref T _) =>
    // TODO: Create InstanceReferenceGeometry from our data
    // need the actual InstanceDefinition object to create InstanceReferenceGeometry
    // this requires definition lookup that depends on the block definition PR
    // For now, return false until we have definition support
    false;

  // TODO: Remove when GrasshopperHelpers.TransformToMatrix is available from merged PR
  private static Matrix4x4 TransformToMatrix(Transform rhinoTransform) =>
    // Simplified version - will be replaced by helper method
    new()
    {
      M11 = rhinoTransform.M00,
      M12 = rhinoTransform.M01,
      M13 = rhinoTransform.M02,
      M14 = rhinoTransform.M03,
      M21 = rhinoTransform.M10,
      M22 = rhinoTransform.M11,
      M23 = rhinoTransform.M12,
      M24 = rhinoTransform.M13,
      M31 = rhinoTransform.M20,
      M32 = rhinoTransform.M21,
      M33 = rhinoTransform.M22,
      M34 = rhinoTransform.M23,
      M41 = rhinoTransform.M30,
      M42 = rhinoTransform.M31,
      M43 = rhinoTransform.M32,
      M44 = rhinoTransform.M33
    };

  public void DrawViewportWires(GH_PreviewWireArgs args) => throw new NotImplementedException();

  public void DrawViewportMeshes(GH_PreviewMeshArgs args) => throw new NotImplementedException();

  public BoundingBox ClippingBox { get; }

  public override IGH_Goo Duplicate() => new SpeckleBlockInstanceWrapperGoo(Value.DeepCopy());

  public SpeckleBlockInstanceWrapperGoo(SpeckleBlockInstanceWrapper value)
  {
    Value = value;
  }
}

public class SpeckleBlockInstanceParam
  : GH_Param<SpeckleBlockInstanceWrapperGoo>,
    IGH_BakeAwareObject,
    IGH_PreviewObject
{
  public SpeckleBlockInstanceParam()
    : this(GH_ParamAccess.item) { }

  public SpeckleBlockInstanceParam(IGH_InstanceDescription tag)
    : base(tag) { }

  public SpeckleBlockInstanceParam(IGH_InstanceDescription tag, GH_ParamAccess access)
    : base(tag, access) { }

  public SpeckleBlockInstanceParam(GH_ParamAccess access)
    : base(
      "Speckle Block Instance", // TODO: claire & bjorn to discuss this wording
      "SBI", // TODO: claire & bjorn to discuss this wording
      "Represents a Speckle block instance",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.PARAMETERS,
      access
    ) { }

  public override Guid ComponentGuid => new("938CCD6E-B202-4A0C-9D68-ABD7683B0EDE");

  protected override Bitmap Icon => Resources.speckle_param_object; // TODO: Create specific icon
  public bool IsBakeCapable => !VolatileData.IsEmpty;
  public bool IsPreviewCapable => !VolatileData.IsEmpty;

  public void BakeGeometry(RhinoDoc doc, List<Guid> obj_ids)
  {
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleBlockInstanceWrapperGoo goo)
      {
        goo.Value.Bake(doc, obj_ids);
      }
    }
  }

  public void BakeGeometry(RhinoDoc doc, ObjectAttributes att, List<Guid> obj_ids) => BakeGeometry(doc, obj_ids); // Instances manage their own attributes

  public void DrawViewportWires(IGH_PreviewArgs args)
  {
    // TODO ?
  }

  public void DrawViewportMeshes(IGH_PreviewArgs args)
  {
    var isSelected = args.Document.SelectedObjects().Contains(this);
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleBlockInstanceWrapperGoo goo)
      {
        goo.Value.DrawPreview(args, isSelected);
      }
    }
  }

  public bool Hidden { get; set; }
  public BoundingBox ClippingBox
  {
    get
    {
      BoundingBox clippingBox = new();
      /*foreach (var item in VolatileData.AllData(true))
      {
        if (item is SpeckleBlockInstanceWrapperGoo goo)
        {
          // calculate transformed bounding box from definition + transform
          // TODO: Add when SpeckleBlockDefinitionWrapper is available (blocked by [CNX-1941](https://linear.app/speckle/issue/CNX-1941/add-speckle-blockdefinition-param))
        }
      }*/
      return clippingBox;
    }
  }
}
