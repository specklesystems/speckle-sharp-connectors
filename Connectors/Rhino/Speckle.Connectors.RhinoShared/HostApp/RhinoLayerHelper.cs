using Rhino;
using Rhino.DocObjects;
using Speckle.Connectors.Rhino.Mapper.Revit;

namespace Speckle.Connectors.Rhino.HostApp;

/// <summary>
/// Helper class for common Rhino layer and object operations.
/// Consolidates layer utilities to eliminate duplication across the codebase.
/// </summary>
public class RhinoLayerHelper
{
  /// <summary>
  /// Gets list of available layers for UI dropdowns.
  /// </summary>
  public LayerOption[] GetAvailableLayers()
  {
    var doc = RhinoDoc.ActiveDoc;
    if (doc == null)
    {
      return [];
    }

    return doc
      .Layers.Where(layer => !layer.IsDeleted)
      .Select(layer => new LayerOption(layer.Id.ToString(), GetFullLayerPath(layer)))
      .OrderBy(layer => layer.Name)
      .ToArray();
  }

  /// <summary>
  /// Gets the full layer path with / delimiter
  /// </summary>
  public string GetFullLayerPath(Layer layer)
  {
    string fullPath = layer.Name;
    Guid parentIndex = layer.ParentLayerId;
    while (parentIndex != Guid.Empty)
    {
      Layer? parentLayer = RhinoDoc.ActiveDoc.Layers.FindId(parentIndex);
      if (parentLayer == null)
      {
        break;
      }

      fullPath = parentLayer.Name + "/" + fullPath; // use "/" delimiter
      parentIndex = parentLayer.ParentLayerId;
    }
    return fullPath;
  }

  /// <summary>
  /// Converts a string layer ID to a Layer.
  /// </summary>
  /// <returns>Layer if found and valid, null otherwise</returns>
  public Layer? GetLayer(string layerIdString) =>
    Guid.TryParse(layerIdString, out var layerId) ? RhinoDoc.ActiveDoc.Layers.FindId(layerId) : null;

  /// <summary>
  /// Helper to check if a layer (by index) has a category mapping.
  /// </summary>
  /// <remarks>
  /// This is arguably a very specific method pertaining to mapper and maybe shouldn't be in this class?
  /// </remarks>
  public bool HasLayerMapping(int layerIndex)
  {
    if (layerIndex < 0 || layerIndex >= RhinoDoc.ActiveDoc.Layers.Count)
    {
      return false;
    }

    var layer = RhinoDoc.ActiveDoc.Layers[layerIndex];
    return !string.IsNullOrEmpty(layer.GetUserString(RevitMappingConstants.CATEGORY_USER_STRING_KEY));
  }

  /// <summary>
  /// Gets all RhinoObjects in the specified layer and all its child layers recursively.
  /// </summary>
  public IEnumerable<RhinoObject> GetObjectsInLayerHierarchy(Layer rootLayer)
  {
    var allObjects = new List<RhinoObject>();
    var layersToSearch = GetLayerAndAllChildren(rootLayer);

    foreach (var layer in layersToSearch)
    {
      var objectsOnLayer = RhinoDoc.ActiveDoc.Objects.FindByLayer(layer);
      allObjects.AddRange(objectsOnLayer);
    }

    return allObjects;
  }

  /// <summary>
  /// Gets the specified layer and all its child layers recursively.
  /// </summary>
  public IEnumerable<Layer> GetLayerAndAllChildren(Layer rootLayer)
  {
    // Return the root layer itself
    yield return rootLayer;

    // Get all child layers recursively
    foreach (var childLayer in GetAllChildLayers(rootLayer))
    {
      yield return childLayer;
    }
  }

  /// <summary>
  /// Recursively gets all child layers of the specified parent layer.
  /// </summary>
  public IEnumerable<Layer> GetAllChildLayers(Layer parentLayer)
  {
    var doc = RhinoDoc.ActiveDoc;
    if (doc?.Layers == null)
    {
      yield break;
    }

    // Find all direct child layers
    var directChildren = doc.Layers.Where(layer => layer.ParentLayerId == parentLayer.Id);

    foreach (var childLayer in directChildren)
    {
      // Return the direct child
      yield return childLayer;

      // Recursively get grandchildren
      foreach (var grandChild in GetAllChildLayers(childLayer))
      {
        yield return grandChild;
      }
    }
  }

  /// <summary>
  /// Checks if a layer is visible by its index.
  /// </summary>
  public bool IsLayerVisible(int layerIndex)
  {
    if (layerIndex < 0 || layerIndex >= RhinoDoc.ActiveDoc.Layers.Count)
    {
      return true; // default to visible for invalid indices (safe fallback)
    }

    var layer = RhinoDoc.ActiveDoc.Layers[layerIndex];
    return layer != null && !layer.IsDeleted && layer.IsVisible;
  }

  /// <summary>
  /// Filters a collection of objects to only include those on visible layers.
  /// </summary>
  public IEnumerable<T> FilterByLayerVisibility<T>(IEnumerable<T> objects)
    where T : RhinoObject => objects.Where(obj => IsLayerVisible(obj.Attributes.LayerIndex));
}
