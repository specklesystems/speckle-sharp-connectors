using Rhino;
using Speckle.Sdk;
using Speckle.Sdk.Models.Collections;
using Layer = Rhino.DocObjects.Layer;
using SpeckleLayer = Speckle.Sdk.Models.Collections.Layer;
#if RHINO8_OR_GREATER
using Rhino.DocObjects;
#endif

namespace Speckle.Connectors.Rhino.HostApp;

/// <summary>
/// Utility class managing layer extraction. Expects to be a scoped dependency per send operation.
/// </summary>
public class RhinoLayerUnpacker
{
  private readonly RhinoLayerHelper _rhinoLayerHelper;
  private readonly Dictionary<int, Collection> _layerCollectionCache = new();

  private static readonly string s_pathSeparator =
#if RHINO8_OR_GREATER
  ModelComponent.NamePathSeparator;
#else
  Layer.PathSeparator;
#endif
  private static readonly string[] s_pathSeparatorSplit = [s_pathSeparator];

  public RhinoLayerUnpacker(RhinoLayerHelper rhinoLayerHelper)
  {
    _rhinoLayerHelper = rhinoLayerHelper;
  }

  /// <summary>
  /// Use this method to get all of the layers that correspond to collection created in the root collection.
  /// </summary>
  /// <returns></returns>
  /// <exception cref="SpeckleException">Throws when a layer could not be retrieved from a stored collection application id</exception>
  public IEnumerable<Layer> GetUsedLayers()
  {
    foreach (string layerId in _layerCollectionCache.Values.Select(o => o.applicationId ?? string.Empty).ToList())
    {
      var layer = _rhinoLayerHelper.GetLayer(layerId);
      if (layer != null)
      {
        yield return layer;
      }
      else if (Guid.TryParse(layerId, out _))
      {
        throw new SpeckleException($"Could not retrieve layer with guid: {layerId}.");
      }
      else
      {
        throw new SpeckleException($"Invalid Collection Layer id: {layerId}. Should be convertible to a Guid.");
      }
    }
  }

  /// <summary>
  /// <para>Use this method to construct the root commit object while converting objects.</para>
  /// <para>Returns the host collection corresponding to the provided layer. If it's the first time that it is being asked for, it will be created and stored in the root object collection.</para>
  /// </summary>
  /// <param name="layer">The layer you want the equivalent collection for.</param>
  /// <param name="rootObjectCollection">The root object that will be sent to Speckle, and will host all collections.</param>
  /// <returns></returns>
  public Collection GetHostObjectCollection(Layer layer, Collection rootObjectCollection)
  {
    if (_layerCollectionCache.TryGetValue(layer.Index, out Collection? value))
    {
      return value;
    }

    var names = layer.FullPath.Split(s_pathSeparatorSplit, StringSplitOptions.None);
    var path = names[0];
    var index = 0;
    var previousCollection = rootObjectCollection;
    foreach (var layerName in names)
    {
      var existingLayerIndex = RhinoDoc.ActiveDoc.Layers.FindByFullPath(path, -1);
      Collection? childCollection = null;
      if (_layerCollectionCache.TryGetValue(existingLayerIndex, out Collection? collection))
      {
        childCollection = collection;
      }
      else
      {
        childCollection = new SpeckleLayer(layerName)
        {
          applicationId = RhinoDoc.ActiveDoc.Layers[existingLayerIndex].Id.ToString()
        };

        previousCollection.elements.Add(childCollection);
        _layerCollectionCache[existingLayerIndex] = childCollection;
      }

      previousCollection = childCollection;

      if (index < names.Length - 1)
      {
        path += s_pathSeparator + names[index + 1];
      }

      index++;
    }

    _layerCollectionCache[layer.Index] = previousCollection;
    return previousCollection;
  }
}
