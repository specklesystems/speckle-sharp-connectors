using Rhino;
using Rhino.DocObjects;
using Speckle.Connectors.Rhino.HostApp;

namespace Speckle.Connectors.Rhino.Mapper.Revit;

/// <summary>
/// Responsible for resolving category mappings from layer hierarchy.
/// Used by the send pipeline to resolve mappings during property extraction.
/// </summary>
/// <remarks>
/// This gets called when no mapping found on the object level.
/// </remarks>
public class RevitMappingResolver
{
  /// <summary>
  /// Gets all objects that would effectively receive the specified layer mapping during send.
  /// Takes into account hierarchical resolution - only returns objects that would actually
  /// resolve to this specific category value through the layer hierarchy.
  /// </summary>
  public string[] GetEffectiveObjectsForLayerMapping(string[] layerIds, string categoryValue)
  {
    var effectiveObjects = new List<string>();

    foreach (var layerId in layerIds)
    {
      var layer = RhinoLayerHelper.GetLayer(layerId);
      if (layer == null)
      {
        continue;
      }

      // Get all objects in this layer and its child layers
      var allObjectsInHierarchy = RhinoLayerHelper.GetObjectsInLayerHierarchy(layer);

      foreach (var obj in allObjectsInHierarchy)
      {
        // Since we're in Layer mode, objects don't have direct mappings
        // Check what category this object would actually resolve to through layer hierarchy
        var resolvedCategory = SearchLayerHierarchyForMapping(obj);

        // Only include if it resolves to THIS specific category
        if (resolvedCategory == categoryValue)
        {
          effectiveObjects.Add(obj.Id.ToString());
        }
      }
    }

    return effectiveObjects.ToArray();
  }

  /// <summary>
  /// Traverses layer hierarchy, returns first mapping found or null
  /// </summary>
  public string? SearchLayerHierarchyForMapping(RhinoObject rhinoObject)
  {
    // NOTE: we agreed on a hierarchical resolution strategy:
    // - Object-level mappings have highest precedence
    // - Layer-level mappings are fallback when no object mapping exists
    // - Traverses layer hierarchy and stops at first mapping found

    var layer = GetLayerByIndex(rhinoObject.Attributes.LayerIndex);
    while (layer != null)
    {
      var layerMapping = layer.GetUserString(RevitMappingConstants.CATEGORY_USER_STRING_KEY);
      if (!string.IsNullOrEmpty(layerMapping))
      {
        return layerMapping; // returns first mapping found
      }

      // move to parent layer
      layer = GetParentLayer(layer);
    }

    return null;
  }

  /// <summary>
  /// Gets a layer by its index from the active doc.
  /// </summary>
  private Layer? GetLayerByIndex(int layerIndex)
  {
    var doc = RhinoDoc.ActiveDoc;
    if (doc?.Layers == null || layerIndex < 0 || layerIndex >= doc.Layers.Count)
    {
      return null;
    }

    return doc.Layers[layerIndex];
  }

  /// <summary>
  /// Gets the parent layer of the given layer.
  /// </summary>
  private Layer? GetParentLayer(Layer layer)
  {
    if (layer.ParentLayerId == Guid.Empty)
    {
      return null; // no parent layer
    }

    var doc = RhinoDoc.ActiveDoc;
    return doc?.Layers?.FindId(layer.ParentLayerId);
  }
}
