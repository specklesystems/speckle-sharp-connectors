using System.Diagnostics.CodeAnalysis;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Connectors.GrasshopperShared.Components;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Properties;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

public class SpeckleBlockInstanceWrapper : SpeckleObjectWrapper
{
  private InstanceProxy _instanceProxy;
  private Transform _transform = Transform.Identity;

  public SpeckleBlockInstanceWrapper() { }

  /// <summary>
  /// A default constructor for speckle block instances, with default values
  /// </summary>
  /// <param name="transform">This should be the identity transform, and will be set as identity regardless of value passed in.</param>
  [SetsRequiredMembers]
  public SpeckleBlockInstanceWrapper(Transform transform)
  {
    // gross af but override the incoming transform to be identity, since this constructor should be a default constructor
    Transform identity = transform == Transform.Identity ? transform : Transform.Identity;

    var units = RhinoDoc.ActiveDoc?.ModelUnitSystem.ToSpeckleString();
    _instanceProxy = new()
    {
      definitionId = "placeholder",
      maxDepth = 0, // represent newly created, top-level objects. actual depth calculation happens in GrasshopperBlockPacker
      transform = GrasshopperHelpers.TransformToMatrix(identity, units),
      units = units ?? Units.None
    };

    Base = _instanceProxy; // set required base
    ApplicationId = Guid.NewGuid().ToString();
    GeometryBase = new InstanceReferenceGeometry(Guid.Empty, identity);
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

  public required Transform Transform
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

  public override string ToString() => $"Speckle Instance Wrapper [{Definition?.Name}]";

  public override IGH_Goo CreateGoo() => new SpeckleBlockInstanceWrapperGoo(this);

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
    new SpeckleBlockInstanceWrapper()
    {
      Base = InstanceProxy.ShallowCopy(),
      GeometryBase = GeometryBase?.Duplicate(),
      Color = Color,
      Material = Material,
      ApplicationId = ApplicationId,
      Parent = Parent,
      Properties = Properties,
      Name = Name,
      Path = Path,
      Transform = Transform, // TODO: note from previous, "Transform will be updated when Base / InstanceProxy is set", but why not copy the transform here??
      Definition = Definition?.DeepCopy(), // block instance specific
    };

  private void UpdateTransformFromProxy()
  {
    _transform = GrasshopperHelpers.MatrixToTransform(_instanceProxy.transform, _instanceProxy.units);
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
        Value = (SpeckleBlockInstanceWrapper)wrapperGoo.Value.DeepCopy();
        return true;
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

  public override IGH_Goo Duplicate() =>
    new SpeckleBlockInstanceWrapperGoo((SpeckleBlockInstanceWrapper)Value.DeepCopy());

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
    var isSelected = args.Document.SelectedObjects().Contains(this) || OwnerSelected();
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleBlockInstanceWrapperGoo goo)
      {
        goo.Value.DrawPreview(args, isSelected);
      }
    }
  }

  private bool OwnerSelected()
  {
    return Attributes?.Parent?.Selected ?? false;
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
