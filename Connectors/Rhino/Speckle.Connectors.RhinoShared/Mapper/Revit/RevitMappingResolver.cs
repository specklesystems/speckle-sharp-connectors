using Rhino;
using Rhino.DocObjects;

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
  private static Layer? GetLayerByIndex(int layerIndex)
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
  private static Layer? GetParentLayer(Layer layer)
  {
    if (layer.ParentLayerId == Guid.Empty)
    {
      return null; // no parent layer
    }

    var doc = RhinoDoc.ActiveDoc;
    return doc?.Layers?.FindId(layer.ParentLayerId);
  }
}
