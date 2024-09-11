using Rhino;
using Speckle.Sdk.Models.Collections;
using Layer = Rhino.DocObjects.Layer;
using SpeckleLayer = Speckle.Sdk.Models.Collections.Layer;

namespace Speckle.Connectors.Rhino.HostApp;

/// <summary>
/// Utility class managing layer extraction. Expects to be a scoped dependency per send operation.
/// </summary>
public class RhinoLayerUnpacker
{
  private readonly Dictionary<int, Collection> _layerCollectionCache = new();

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

    var names = layer.FullPath.Split(new[] { Layer.PathSeparator }, StringSplitOptions.None);
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
        path += Layer.PathSeparator + names[index + 1];
      }

      index++;
    }

    _layerCollectionCache[layer.Index] = previousCollection;
    return previousCollection;
  }
}
