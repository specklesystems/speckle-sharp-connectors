using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Connectors.GrasshopperShared.Components;
using Speckle.Connectors.GrasshopperShared.Properties;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

/// <summary>
/// Wrapper around a block definition and its converted Speckle equivalent.
/// </summary>
public class SpeckleBlockDefinitionWrapper : SpeckleWrapper
{
  public InstanceDefinitionProxy InstanceDefinitionProxy { get; set; }

  public override required Base Base
  {
    get => InstanceDefinitionProxy;
    set
    {
      if (value is not InstanceDefinitionProxy def)
      {
        throw new ArgumentException("Cannot create block definition wrapper from a non-InstanceDefinitionProxy Base");
      }
      InstanceDefinitionProxy = def;
    }
  }

  private List<SpeckleObjectWrapper> _objects = new();

  public List<SpeckleObjectWrapper> Objects
  {
    get => _objects;
    set
    {
      ValidateObjects(value);
      _objects = value;
    }
  }

  private static void ValidateObjects(List<SpeckleObjectWrapper> objects)
  {
    // SpeckleBlockInstanceWrapper inherits from SpeckleObjectWrapper, check if it's assignable, not exact type match
    var invalidObjects = objects.Where(o => !typeof(SpeckleObjectWrapper).IsAssignableFrom(o.GetType())).ToList();

    if (invalidObjects.Count > 0)
    {
      var invalidTypes = string.Join(", ", invalidObjects.Select(o => o.GetType().Name));
      throw new ArgumentException(
        $"Block definitions can only contain objects and instances. Found invalid types: {invalidTypes}"
      );
    }
  }

  public override string ToString() => $"Speckle Block Definition [{Name}]";

  /// <summary>
  /// Creates a preview of the block definition by displaying all contained objects
  /// </summary>
  /// <remarks>
  /// Leveraging already defined preview logic for the objects which make up this block. Refer to <see cref="SpeckleObjectWrapper.DrawPreview"/>.
  /// </remarks>
  public void DrawPreview(IGH_PreviewArgs args, bool isSelected = false)
  {
    foreach (var obj in Objects)
    {
      obj.DrawPreview(args, isSelected);
    }
  }

  public void DrawPreviewRaw(DisplayPipeline display, DisplayMaterial material) // TODO: what materials are here??
  {
    foreach (var obj in Objects)
    {
      if (obj.GeometryBase != null)
      {
        obj.DrawPreviewRaw(display, material);
      }
    }
  }

  /// <summary>
  /// Bakes this block definition to Rhino, automatically handing nested block dependencies.
  /// Nested definitions are baked first, to ensure that InstanceReferenceGeometry can find them
  /// </summary>
  public (int index, bool existingDefinitionUpdated) Bake(RhinoDoc doc, List<Guid> objIds, string? name = null)
  {
    // Collect and bake nested dependencies first
    var visited = new HashSet<string>();
    BakeNestedDefinitions(this, doc, objIds, visited);

    // Now bake this definition
    return BakeCurrentDefinition(doc, objIds, name);
  }

  /// <summary>
  /// Recursively bakes nested block definitions in dependency order.
  /// </summary>
  private static void BakeNestedDefinitions(
    SpeckleBlockDefinitionWrapper definition,
    RhinoDoc doc,
    List<Guid> objIds,
    HashSet<string> visited
  )
  {
    var defId = definition.ApplicationId ?? definition.Name;
    if (visited.Contains(defId))
    {
      return;
    }

    visited.Add(defId);

    // Bake nested dependencies first
    foreach (var obj in definition.Objects)
    {
      if (obj is SpeckleBlockInstanceWrapper { Definition: not null } blockInstance)
      {
        BakeNestedDefinitions(blockInstance.Definition, doc, objIds, visited);

        // Bake the nested definition if not already in document
        if (doc.InstanceDefinitions.Find(blockInstance.Definition.Name) == null)
        {
          blockInstance.Definition.BakeCurrentDefinition(doc, objIds);
        }
      }
    }
  }

  /// <summary>
  /// Creates the Rhino InstanceDefinition, converting nested instances to InstanceReferenceGeometry.
  /// </summary>
  private (int index, bool existingDefinitionUpdated) BakeCurrentDefinition(
    RhinoDoc doc,
    List<Guid> objIds,
    string? name = null
  )
  {
    string definitionName = name ?? Name;
    var geometries = new List<GeometryBase>();
    var attributes = new List<ObjectAttributes>();

    foreach (var obj in Objects)
    {
      if (obj is SpeckleBlockInstanceWrapper blockInstance && blockInstance.Definition != null)
      {
        // Convert to InstanceReferenceGeometry (nested definition should exist by now)
        var referenceDefinition = doc.InstanceDefinitions.Find(blockInstance.Definition.Name);
        if (referenceDefinition != null)
        {
          geometries.Add(new InstanceReferenceGeometry(referenceDefinition.Id, blockInstance.Transform));
          attributes.Add(blockInstance.CreateObjectAttributes(bakeMaterial: true));
        }
      }
      else if (obj.GeometryBase != null)
      {
        geometries.Add(obj.GeometryBase);
        attributes.Add(obj.CreateObjectAttributes(bakeMaterial: true));
      }
    }

    if (geometries.Count == 0)
    {
      return (-1, false);
    }

    var documentDefinition = doc.InstanceDefinitions.Find(definitionName);

    if (documentDefinition != null)
    {
      // Update existing
      bool success = doc.InstanceDefinitions.ModifyGeometry(
        documentDefinition.Index,
        geometries.ToArray(),
        attributes.ToArray()
      );
      if (success)
      {
        objIds.Add(documentDefinition.Id);
        return (documentDefinition.Index, true);
      }
      return (-1, true);
    }

    // Create new
    int index = doc.InstanceDefinitions.Add(definitionName, string.Empty, Point3d.Origin, geometries, attributes);
    if (index >= 0)
    {
      objIds.Add(doc.InstanceDefinitions[index].Id);
      return (index, false);
    }
    return (-1, false);
  }

  public SpeckleBlockDefinitionWrapper DeepCopy() =>
    new()
    {
      Base = InstanceDefinitionProxy.ShallowCopy(),
      /*Color = Color,
      Material = Material,*/
      ApplicationId = ApplicationId,
      Name = Name,
      Objects = Objects.Select(o => o.DeepCopy()).ToList()
    };
}

public partial class SpeckleBlockDefinitionWrapperGoo : GH_Goo<SpeckleBlockDefinitionWrapper>
{
  public override IGH_Goo Duplicate() => new SpeckleBlockDefinitionWrapperGoo(Value.DeepCopy());

  public override string ToString() => $@"Speckle Block Definition Goo : {Value.Name}";

  public override bool IsValid => Value?.InstanceDefinitionProxy is not null;
  public override string TypeName => "Speckle block definition wrapper";
  public override string TypeDescription => "A wrapper around speckle instance definition proxies.";

  public override bool CastFrom(object source)
  {
    switch (source)
    {
      case SpeckleBlockDefinitionWrapper wrapper:
        Value = wrapper.DeepCopy();
        return true;

      case GH_Goo<SpeckleBlockDefinitionWrapper> blockDefinitionGoo:
        Value = blockDefinitionGoo.Value.DeepCopy();
        return true;

      case InstanceDefinitionProxy speckleInstanceDefProxy:
        Value = new SpeckleBlockDefinitionWrapper()
        {
          Base = speckleInstanceDefProxy,
          Name = speckleInstanceDefProxy.name,
          ApplicationId = speckleInstanceDefProxy.applicationId ?? Guid.NewGuid().ToString()
        };
        return true;
    }

    // Rhino 8 Model Objects
    return CastFromModelObject(source);
  }

#if !RHINO8_OR_GREATER
  private bool CastFromModelObject(object _) => false;

  private bool CastToModelObject<T>(ref T _) => false;
#endif

  public override bool CastTo<T>(ref T target)
  {
    var type = typeof(T);

    if (type == typeof(InstanceDefinitionProxy))
    {
      target = (T)(object)Value.InstanceDefinitionProxy;
      return true;
    }

    return CastToModelObject(ref target);
  }

  /// <summary>
  /// Creates a deep copy of this block definition wrapper for proper data handling.
  /// Follows the same pattern as other Goo implementations in the codebase.
  /// </summary>
  /// <returns>A new instance with copied data</returns>
  public SpeckleBlockDefinitionWrapper DeepCopy() =>
    new()
    {
      Base = Value.InstanceDefinitionProxy.ShallowCopy(),
      Name = Value.Name,
      Objects = Value.Objects.Select(o => o.DeepCopy()).ToList(),
      ApplicationId = Value.ApplicationId
    };

  public SpeckleBlockDefinitionWrapperGoo(SpeckleBlockDefinitionWrapper value)
  {
    Value = value;
  }

  public SpeckleBlockDefinitionWrapperGoo()
  {
    Value = new()
    {
      Base = new InstanceDefinitionProxy
      {
        name = "Unnamed Block",
        objects = new List<string>(),
        maxDepth = 0 // represent newly created, top-level objects. actual depth calculation happens in GrasshopperBlockPacker
      },
    };
  }
}

public class SpeckleBlockDefinitionWrapperParam
  : GH_Param<SpeckleBlockDefinitionWrapperGoo>,
    IGH_BakeAwareObject,
    IGH_PreviewObject
{
  public SpeckleBlockDefinitionWrapperParam()
    : this(GH_ParamAccess.item) { }

  public SpeckleBlockDefinitionWrapperParam(IGH_InstanceDescription tag)
    : base(tag) { }

  public SpeckleBlockDefinitionWrapperParam(IGH_InstanceDescription tag, GH_ParamAccess access)
    : base(tag, access) { }

  public SpeckleBlockDefinitionWrapperParam(GH_ParamAccess access)
    : base(
      "Speckle Block Definition",
      "SBD",
      "Returns a Speckle Block definition.",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.PARAMETERS,
      access
    ) { }

  public override Guid ComponentGuid => new("C71BE6AD-E27B-4E7F-87DA-569D4DEE77BE");

  protected override Bitmap Icon => Resources.speckle_param_block_def;

  public override void RegisterRemoteIDs(GH_GuidTable idList)
  {
    // Register Rhino InstanceDefinition GUIDs so Grasshopper can track when
    // block definitions change in the Rhino document and auto-expire this parameter
    foreach (var item in VolatileData.AllData(true))
    {
      if (
        item is SpeckleBlockDefinitionWrapperGoo goo
        && goo.Value?.ApplicationId != null
        && Guid.TryParse(goo.Value.ApplicationId, out Guid id)
      )
      {
        idList.Add(id, this);
      }
    }
  }

  public bool IsBakeCapable => !VolatileData.IsEmpty;
  public bool IsPreviewCapable => !VolatileData.IsEmpty;

  public void BakeGeometry(RhinoDoc doc, List<Guid> objIds) => BakeAllItems(doc, objIds);

  public void BakeGeometry(RhinoDoc doc, ObjectAttributes att, List<Guid> objIds) =>
    // we "ignore" the ObjectAttributes parameter because definitions manage their own internal object attributes
    // atts aren't on a definition level, but either on an instance level and/or objects within a definition (right?)
    BakeAllItems(doc, objIds);

  private void BakeAllItems(RhinoDoc doc, List<Guid> objIds)
  {
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleBlockDefinitionWrapperGoo goo)
      {
        var (index, wasUpdated) = goo.Value.Bake(doc, objIds);

        if (index != -1)
        {
          string message = wasUpdated
            ? $"Updated existing block definition: '{goo.Value.Name}'"
            : $"Created new block definition: '{goo.Value.Name}'";
          AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, message);
        }
        else
        {
          AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Failed to bake block definition: '{goo.Value.Name}'");
        }
      }
    }
  }

  public void DrawViewportWires(IGH_PreviewArgs args)
  {
    // TODO: Do block definitions even have separate wire preview?
  }

  public void DrawViewportMeshes(IGH_PreviewArgs args)
  {
    var isSelected = args.Document.SelectedObjects().Contains(this) || OwnerSelected();
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleBlockDefinitionWrapperGoo goo)
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
      BoundingBox clippingBox = new();
      foreach (var item in VolatileData.AllData(true))
      {
        if (item is SpeckleBlockDefinitionWrapperGoo goo && goo.Value?.Objects != null)
        {
          foreach (var obj in goo.Value.Objects)
          {
            if (obj.GeometryBase != null)
            {
              clippingBox.Union(obj.GeometryBase.GetBoundingBox(false));
            }
          }
        }
      }
      return clippingBox;
    }
  }
}
