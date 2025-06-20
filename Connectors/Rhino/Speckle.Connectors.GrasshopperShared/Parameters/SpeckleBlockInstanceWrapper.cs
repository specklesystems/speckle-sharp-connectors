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

namespace Speckle.Connectors.GrasshopperShared.Parameters;

public class SpeckleBlockInstanceWrapper : SpeckleObjectWrapper
{
  private InstanceProxy _instanceProxy;
  private Transform _transform = Transform.Identity;

  public SpeckleBlockInstanceWrapper()
  {
    var units = RhinoDoc.ActiveDoc?.ModelUnitSystem.ToSpeckleString() ?? "none";
    _instanceProxy = new InstanceProxy
    {
      definitionId = "placeholder",
      maxDepth = 0, // represent newly created, top-level objects. actual depth calculation happens in GrasshopperBlockPacker
      transform = GrasshopperHelpers.TransformToMatrix(Transform.Identity, units),
      units = units,
      applicationId = Guid.NewGuid().ToString() // Base needs valid proxy (wrapper sync demands immediate valid id)
    };

    Base = _instanceProxy; // set required base
    GeometryBase = null; // block instances typically don't have direct geometry
  }

  public InstanceProxy InstanceProxy
  {
    get => _instanceProxy;
    set
    {
      _instanceProxy = value ?? throw new ArgumentNullException(nameof(value));
      Base = _instanceProxy; // keep base in sync
      UpdateTransformFromProxy();
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

  public override required Base Base
  {
    get => _instanceProxy;
    set
    {
      if (value is not InstanceProxy proxy)
      {
        throw new ArgumentException("Cannot create block instance wrapper from a non-InstanceProxy Base");
      }

      _instanceProxy = proxy;
      UpdateTransformFromProxy();
    }
  }

  // NOTE: GeometryBase from SpeckleObjectWrapper can be:
  // - null for pure instances
  // - OR flattened geometry for preview (decide this later)

  // TODO: These are now inherited from SpeckleObjectWrapper - no implementation needed for now
  // public Color? Color { get; set; }  // inherited from SpeckleObjectWrapper
  // public SpeckleMaterialWrapper? Material { get; set; }  // inherited from SpeckleObjectWrapper

  public override string ToString() => $"Speckle Block Instance [{Name}]";

  public override void DrawPreview(IGH_PreviewArgs args, bool isSelected = false)
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
          ApplicationId = obj.ApplicationId
        };

        tempWrapper.DrawPreview(args, isSelected);
      }
    }
  }

  // TODO: public void DrawPreviewRaw() ?

  public override void Bake(RhinoDoc doc, List<Guid> objIds, int bakeLayerIndex = -1, bool layersAlreadyCreated = false)
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

    var attributes = CreateObjectAttributes(bakeLayerIndex, true);

    // create the instance with our specific transform
    var instanceRef = doc.Objects.AddInstanceObject(existingDef.Index, Transform, attributes);
    if (instanceRef != Guid.Empty)
    {
      objIds.Add(instanceRef);
    }
  }

  public override SpeckleObjectWrapper DeepCopy() =>
    new SpeckleBlockInstanceWrapper
    {
      Base = InstanceProxy.ShallowCopy(),
      GeometryBase = GeometryBase?.Duplicate(),
      Color = null, // TODO: commented out in props
      Material = null, // TODO: commented out in props
      ApplicationId = ApplicationId,
      Parent = Parent,
      Properties = Properties,
      Name = Name,
      Path = Path,
      Definition = Definition?.DeepCopy(), // block instance specific
      // Transform will be updated when Base / InstanceProxy is set
    };

  public override IGH_Goo CreateGoo() => new SpeckleBlockInstanceWrapperGoo(this);

  /// <summary>
  /// Creates a new SpeckleBlockInstanceWrapper with default values, centralizing creation logic.
  /// </summary>
  /// <remarks>Factory pattern alleviates code duplication, this was repeated three times previously!</remarks>
  public static SpeckleBlockInstanceWrapper CreateDefault(string? name = null)
  {
    var units = RhinoDoc.ActiveDoc?.ModelUnitSystem.ToSpeckleString() ?? "none";
    var appId = Guid.NewGuid().ToString();

    return new SpeckleBlockInstanceWrapper
    {
      Base = new InstanceProxy
      {
        definitionId = "placeholder",
        maxDepth = 0, // represent newly created, top-level objects. actual depth calculation happens in GrasshopperBlockPacker
        transform = GrasshopperHelpers.TransformToMatrix(Transform.Identity, units),
        units = units,
        applicationId = appId
      },
      GeometryBase = null,
      Name = name ?? "Block Instance",
      ApplicationId = appId
    };
  }

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
}

public partial class SpeckleBlockInstanceWrapperGoo : GH_Goo<SpeckleBlockInstanceWrapper>, IGH_PreviewData
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
        Value = (SpeckleBlockInstanceWrapper)wrapper.DeepCopy();
        return true;

      case GH_Goo<SpeckleBlockInstanceWrapper> wrapperGoo:
        if (wrapperGoo.Value != null)
        {
          Value = (SpeckleBlockInstanceWrapper)wrapperGoo.Value.DeepCopy();
          return true;
        }
        return false;

      case InstanceProxy instanceProxy:
        Value = new SpeckleBlockInstanceWrapper
        {
          Base = instanceProxy,
          GeometryBase = null,
          ApplicationId = instanceProxy.applicationId ?? Guid.NewGuid().ToString(),
          Name = "Block Instance"
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

  public override IGH_Goo Duplicate() =>
    new SpeckleBlockInstanceWrapperGoo((SpeckleBlockInstanceWrapper)Value.DeepCopy());

  // NOTE: parameterless constructor should only be used for casting
  public SpeckleBlockInstanceWrapperGoo()
  {
    Value = SpeckleBlockInstanceWrapper.CreateDefault();
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

  protected override Bitmap Icon => Resources.speckle_param_block_instance;

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
