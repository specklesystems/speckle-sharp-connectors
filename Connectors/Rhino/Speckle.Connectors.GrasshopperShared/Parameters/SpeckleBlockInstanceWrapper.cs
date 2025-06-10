using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Connectors.GrasshopperShared.Components;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Properties;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Instances;
using Plane = Rhino.Geometry.Plane;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

/// <summary>
/// A Wrapper class representing a block instance.
///
/// Works reliably for:
/// - Pure Speckle workflows (definition ↔ instance)
/// - Rhino → Grasshopper (document-based definitions)
/// - Preview and baking (when definition is available)
///
/// Limitations:
/// - Round-trip through Grasshopper's native block system may lose geometry
/// - Casting to GH_InstanceReference works but results in points on bake
/// </summary>
public class SpeckleBlockInstanceWrapper : SpeckleWrapper
{
  private InstanceProxy _instanceProxy;
  private Transform _transform = Transform.Identity;

  public override string ToString() => $"Speckle Block Instance [{Name}]";

  public List<string> Path { get; set; } = new();
  public SpeckleCollectionWrapper? Parent { get; set; }
  public SpecklePropertyGroupGoo Properties { get; set; } = new();

  public InstanceProxy InstanceProxy
  {
    get => _instanceProxy;
    set
    {
      _instanceProxy = value ?? throw new ArgumentNullException(nameof(value));
      UpdateTransformFromProxy();
    }
  }
  public override Base Base // NOTE: `InstanceProxy` wraps `Base` just like `SpeckleCollectionWrapper` and `SpeckleMaterialWrapper`
  {
    get => InstanceProxy;
    set
    {
      if (value is not InstanceProxy proxy)
      {
        throw new ArgumentException("Cannot create block instance wrapper from a non-InstanceProxy Base");
      }
      InstanceProxy = proxy;
    }
  }

  public SpeckleBlockDefinitionWrapper? Definition { get; set; }

  public Transform Transform
  {
    get => _transform;
    set
    {
      _transform = value;
      UpdateProxyFromTransform();
    }
  }

  // TODO: we need to wait on this. not sure how to tackle this 🤯 overrides etc.
  /*public Color? Color { get; set; }
  public SpeckleMaterialWrapper? Material { get; set; }*/


  private void UpdateTransformFromProxy()
  {
    var units = _instanceProxy.units;
    _transform = GrasshopperHelpers.MatrixToTransform(_instanceProxy.transform, units);
  }

  private void UpdateProxyFromTransform()
  {
    var units = _instanceProxy.units;
    _instanceProxy.transform = GrasshopperHelpers.TransformToMatrix(_transform, units);
    _instanceProxy.units = units;
  }

  public void DrawPreview(IGH_PreviewArgs args, bool isSelected = false)
  {
    if (Definition?.Objects == null)
    {
      return;
    }

    foreach (var obj in Definition.Objects)
    {
      if (obj.GeometryBase != null)
      {
        var transformedGeometry = obj.GeometryBase.Duplicate();
        transformedGeometry.Transform(Transform);

        var tempWrapper = new SpeckleObjectWrapper
        {
          Base = obj.Base,
          GeometryBase = transformedGeometry,
          Color = obj.Color,
          Material = obj.Material,
          Properties = obj.Properties,
          Name = obj.Name,
          WrapperGuid = obj.WrapperGuid,
          ApplicationId = obj.ApplicationId
        };

        tempWrapper.DrawPreview(args, isSelected);
      }
    }
  }

  public void Bake(RhinoDoc doc, List<Guid> objIds, int bakeLayerIndex = -1)
  {
    if (Definition?.Objects == null)
    {
      return; // can't bake an instance without a definition
    }

    // check if the definition already exists in the document
    // this prevents multiple instances from overwriting the same definition
    var existingDef = doc.InstanceDefinitions.Find(Definition.Name);

    if (existingDef == null)
    {
      // definition doesn't exist yet, create it
      // this should only happen for the first instance with this definition name
      var (index, _) = Definition.Bake(doc, objIds);
      if (index == -1)
      {
        return; // definition creation failed
      }
      existingDef = doc.InstanceDefinitions[index];
    }

    // now create the actual instance using the definition
    var attributes = new ObjectAttributes { Name = Name };

    if (bakeLayerIndex >= 0)
    {
      attributes.LayerIndex = bakeLayerIndex;
    }

    // copy properties as user strings onto the instance
    foreach (var kvp in Properties.Value)
    {
      attributes.SetUserString(kvp.Key, kvp.Value.Value?.ToString() ?? "");
    }

    // create the instance with our specific transform
    var instanceRef = doc.Objects.AddInstanceObject(existingDef.Index, Transform, attributes);
    if (instanceRef != Guid.Empty)
    {
      objIds.Add(instanceRef);
    }
  }

  public SpeckleBlockInstanceWrapper DeepCopy() =>
    new()
    {
      Base = InstanceProxy.ShallowCopy(),
      Definition = Definition?.DeepCopy(),
      Transform = _transform,
      Properties = Properties,
      ApplicationId = ApplicationId,
      Name = Name,
      Path = Path,
      Parent = Parent
    };

  // Constructor ensures _instanceProxy is never null
  public SpeckleBlockInstanceWrapper()
  {
    var units = RhinoDoc.ActiveDoc?.ModelUnitSystem.ToSpeckleString() ?? "none";
    _instanceProxy = new InstanceProxy
    {
      definitionId = "placeholder",
      maxDepth = 1,
      transform = GrasshopperHelpers.TransformToMatrix(Transform.Identity, units),
      units = units,
      applicationId = Guid.NewGuid().ToString()
    };
  }
}

public partial class SpeckleBlockInstanceWrapperGoo : GH_Goo<SpeckleBlockInstanceWrapper>, IGH_PreviewData, ISpeckleGoo
{
  public override string ToString() => $@"Speckle Block Instance Goo ({m_value.Definition?.Name ?? null})";

  public override bool IsValid => Value?.InstanceProxy != null;
  public override string TypeName => "Speckle block instance wrapper";
  public override string TypeDescription => "A wrapper around speckle grasshopper block instances.";

  public override bool CastFrom(object source)
  {
    switch (source)
    {
      case SpeckleBlockInstanceWrapper wrapper:
        Value = wrapper.DeepCopy();
        return true;

      case GH_Goo<SpeckleBlockInstanceWrapper> wrapperGoo:
        if (wrapperGoo.Value != null)
        {
          Value = wrapperGoo.Value.DeepCopy();
          return true;
        }
        return false;

      case Transform transform:
        return CreateFromTransform(transform);
      case Plane plane:
        var planeTransform = Transform.PlaneToPlane(Plane.WorldXY, plane);
        return CreateFromTransform(planeTransform);

      case InstanceProxy instanceProxy:
        Value = new SpeckleBlockInstanceWrapper
        {
          InstanceProxy = instanceProxy,
          ApplicationId = instanceProxy.applicationId ?? Guid.NewGuid().ToString()
        };
        return true;
    }
    return CastFromModelObject(source);
  }

  public override bool CastTo<T>(ref T target)
  {
    if (Value == null)
    {
      return false;
    }

    switch (target)
    {
      case Transform:
        target = (T)(object)Value.Transform;
        return true;

      case InstanceProxy:
        target = (T)(object)Value.InstanceProxy;
        return true;

      default:
        return CastToModelObject(ref target);
    }
  }

#if !RHINO8_OR_GREATER
  private bool CastFromModelObject(object _) => false;

  private bool CastToModelObject<T>(ref T _) => false;
#endif

  private bool CreateFromTransform(Transform transform)
  {
    Value ??= new SpeckleBlockInstanceWrapper();

    Value.Transform = transform;
    return true;
  }

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

  public override IGH_Goo Duplicate() => new SpeckleBlockInstanceWrapperGoo(Value.DeepCopy());

  public SpeckleBlockInstanceWrapperGoo()
  {
    Value = new SpeckleBlockInstanceWrapper { Name = "Block Instance", ApplicationId = Guid.NewGuid().ToString() };
  }

  public SpeckleBlockInstanceWrapperGoo(SpeckleBlockInstanceWrapper value)
  {
    Value = value ?? throw new ArgumentNullException(nameof(value));
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
      "Speckle Block Instance",
      "SBI",
      "Represents a Speckle block instance",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.PARAMETERS,
      access
    ) { }

  public override Guid ComponentGuid => new("938CCD6E-B202-4A0C-9D68-ABD7683B0EDE");

  protected override Bitmap Icon => Resources.speckle_param_object; // TODO: Create specific icon

  public override void RegisterRemoteIDs(GH_GuidTable idList)
  {
    // Register both the block definition and instance GUIDs so Grasshopper
    // auto-expires when either the definition or instance changes in Rhino
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleBlockInstanceWrapperGoo goo)
      {
        // Track the referenced block definition
        if (
          goo.Value?.Definition?.ApplicationId != null
          && Guid.TryParse(goo.Value.Definition.ApplicationId, out Guid defId)
        )
        {
          idList.Add(defId, this);
        }

        // Track the instance itself if it references a Rhino object
        if (goo.Value?.ApplicationId != null && Guid.TryParse(goo.Value.ApplicationId, out Guid instId))
        {
          idList.Add(instId, this);
        }
      }
    }
  }

  public bool IsBakeCapable => !VolatileData.IsEmpty;
  public bool IsPreviewCapable => !VolatileData.IsEmpty;

  public void BakeGeometry(RhinoDoc doc, List<Guid> objIds)
  {
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleBlockInstanceWrapperGoo goo)
      {
        goo.Value.Bake(doc, objIds);
      }
    }
  }

  public void BakeGeometry(RhinoDoc doc, ObjectAttributes att, List<Guid> objIds) => BakeGeometry(doc, objIds); // Instances manage their own attributes

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
      var clippingBox = new BoundingBox();
      foreach (var item in VolatileData.AllData(true))
      {
        if (item is SpeckleBlockInstanceWrapperGoo goo)
        {
          clippingBox.Union(goo.ClippingBox);
        }
      }
      return clippingBox;
    }
  }
}
