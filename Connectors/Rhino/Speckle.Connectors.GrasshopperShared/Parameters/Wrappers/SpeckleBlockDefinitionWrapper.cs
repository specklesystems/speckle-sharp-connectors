using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

/// <summary>
/// Wrapper around a block definition and its converted Speckle equivalent.
/// </summary>
public class SpeckleBlockDefinitionWrapper : SpeckleWrapper
{
  public InstanceDefinitionProxy InstanceDefinitionProxy { get; set; }
  private const int MAX_DISPLAY_DEPTH = 3;

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

  public override string ToString() => $"Speckle Block Definition Wrapper [{Name}({Objects.Count})]";

  public override IGH_Goo CreateGoo() => new SpeckleBlockDefinitionWrapperGoo(this);

  /// <summary>
  /// Creates a preview of the block definition by displaying all contained objects
  /// </summary>
  /// <remarks>
  /// Leveraging already defined preview logic for the objects which make up this block. Refer to <see cref="SpeckleObjectWrapper.DrawPreview"/>.
  /// </remarks>
  public void DrawPreview(IGH_PreviewArgs args, bool isSelected = false) =>
    DrawDepthLimitedPreview(args, isSelected, 0);

  private void DrawDepthLimitedPreview(IGH_PreviewArgs args, bool isSelected, int depth)
  {
    if (depth > MAX_DISPLAY_DEPTH)
    {
      return; // Stop early if too deep
    }

    foreach (var obj in Objects)
    {
      if (obj is SpeckleBlockInstanceWrapper nestedInstance)
      {
        nestedInstance.DrawDepthLimitedPreview(args, isSelected, depth + 1);
      }
      else
      {
        obj.DrawPreview(args, isSelected);
      }
    }
  }

  public void DrawPreviewRaw(DisplayPipeline display, DisplayMaterial material) =>
    DrawDepthLimitedPreviewRaw(display, material, 0);

  private void DrawDepthLimitedPreviewRaw(DisplayPipeline display, DisplayMaterial material, int depth)
  {
    if (depth > MAX_DISPLAY_DEPTH)
    {
      return; // Stop early if too deep
    }

    foreach (var obj in Objects)
    {
      if (obj is SpeckleBlockInstanceWrapper nestedInstance)
      {
        nestedInstance.DrawDepthLimitedPreviewRaw(display, material, depth + 1);
      }
      else
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
      ApplicationId = ApplicationId,
      Name = Name,
      Objects = Objects.Select(o => o.DeepCopy()).ToList()
    };
}
