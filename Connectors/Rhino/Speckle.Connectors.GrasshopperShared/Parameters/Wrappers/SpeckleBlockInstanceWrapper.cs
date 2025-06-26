using System.Diagnostics.CodeAnalysis;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

public class SpeckleBlockInstanceWrapper : SpeckleObjectWrapper
{
  private InstanceProxy _instanceProxy;
  private Transform _transform = Transform.Identity;
  private SpeckleBlockDefinitionWrapper? _definition;

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

  public SpeckleBlockDefinitionWrapper? Definition
  {
    get => _definition;
    set
    {
      _definition = value;

      if (_definition != null)
      {
        _instanceProxy.definitionId =
          _definition.ApplicationId
          ?? throw new InvalidOperationException(
            "Block definition must have ApplicationId before being assigned to instance"
          );
      }
    }
  }

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
      Transform = Transform,
      Definition = Definition?.DeepCopy(),
    };

  private void UpdateTransformFromProxy() =>
    _transform = GrasshopperHelpers.MatrixToTransform(_instanceProxy.transform, _instanceProxy.units);

  private void UpdateProxyFromTransform()
  {
    var units = _instanceProxy.units;
    _instanceProxy.transform = GrasshopperHelpers.TransformToMatrix(_transform, units);
    _instanceProxy.units = units;
  }
}
