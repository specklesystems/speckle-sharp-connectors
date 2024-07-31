using System.Diagnostics.Contracts;
using Rhino;
using Speckle.Core.Models.Collections;
using Speckle.Core.Models.GraphTraversal;
using Layer = Rhino.DocObjects.Layer;
using SpeckleLayer = Speckle.Core.Models.Collections.Layer;

namespace Speckle.Connectors.Rhino.HostApp;

/// <summary>
/// Utility class managing layer creation and/or extraction from rhino. Expects to be a scoped dependency per send or receive operation.
/// </summary>
public class RhinoLayerManager
{
  private readonly Dictionary<string, int> _hostLayerCache = new();
  private readonly Dictionary<int, Collection> _layerCollectionCache = new();

  /// <summary>
  /// Creates the base layer and adds it to the cache.
  /// </summary>
  /// <param name="baseLayerName"></param>
  public void CreateBaseLayer(string baseLayerName)
  {
    var index = RhinoDoc.ActiveDoc.Layers.Add(new Layer { Name = baseLayerName }); // POC: too much effort right now to wrap around the interfaced layers and doc
    // var index = _contextStack.Current.Document.Layers.Add(new Layer { Name = baseLayerName });
    _hostLayerCache.Add(baseLayerName, index);
  }

  /// <summary>
  /// <para>For receive: Use this method to construct layers in the host app when receiving.</para>.
  /// </summary>
  public int GetAndCreateLayerFromPath(Collection[] collectionPath, string baseLayerName, out bool isNewLayer)
  {
    isNewLayer = true;
    var layerPath = collectionPath.Select(o => string.IsNullOrWhiteSpace(o.name) ? "unnamed" : o.name);
    var layerFullName = string.Join(Layer.PathSeparator, layerPath);

    if (_hostLayerCache.TryGetValue(layerFullName, out int existingLayerIndex))
    {
      isNewLayer = false;
      return existingLayerIndex;
    }

    var currentLayerName = baseLayerName;
    var currentDocument = RhinoDoc.ActiveDoc; // POC: too much effort right now to wrap around the interfaced layers
    var previousLayer = currentDocument.Layers.FindName(currentLayerName);
    foreach (Collection collection in collectionPath)
    {
      currentLayerName = baseLayerName + Layer.PathSeparator + collection.name;
      currentLayerName = currentLayerName.Replace("{", "").Replace("}", ""); // Rhino specific cleanup for gh (see RemoveInvalidRhinoChars)
      if (_hostLayerCache.TryGetValue(currentLayerName, out int value))
      {
        previousLayer = currentDocument.Layers.FindIndex(value);
        continue;
      }

      var cleanNewLayerName = collection.name.Replace("{", "").Replace("}", "");
      Color layerColor = collection is IHasColor coloredCollection
        ? Color.FromArgb(coloredCollection.color)
        : Color.Black;
      var newLayer = new Layer
      {
        Name = cleanNewLayerName,
        ParentLayerId = previousLayer.Id,
        Color = layerColor
      };

      var index = currentDocument.Layers.Add(newLayer);
      _hostLayerCache.Add(currentLayerName, index);
      previousLayer = currentDocument.Layers.FindIndex(index); // note we need to get the correct id out, hence why we're double calling this
    }
    return previousLayer.Index;
  }

  /// <summary>
  /// <para>For send: Use this method to construct the root commit object while converting objects.</para>
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
      var rhLayer = RhinoDoc.ActiveDoc.Layers[existingLayerIndex];
      Collection? childCollection = null;
      if (_layerCollectionCache.TryGetValue(existingLayerIndex, out Collection? collection))
      {
        childCollection = collection;
      }
      else
      {
        childCollection = new SpeckleLayer(layerName, rhLayer.Color.ToArgb())
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

  /// <summary>
  /// Gets the full path of the layer, concatenated with Rhino's Layer.PathSeparator
  /// </summary>
  /// <param name="context"></param>
  /// <returns></returns>
  [Pure]
  public Collection[] GetLayerPath(TraversalContext context)
  {
    Collection[] collectionBasedPath = context.GetAscendantOfType<Collection>().Reverse().ToArray();

    Collection[] collectionPath =
      collectionBasedPath.Length != 0
        ? collectionBasedPath
        : context.GetPropertyPath().Reverse().Select(o => new Collection() { name = o }).ToArray();

    return collectionPath;
  }
}
