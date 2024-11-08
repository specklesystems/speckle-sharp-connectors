using Rhino;
using Rhino.DocObjects;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Sdk;
using Speckle.Sdk.Models.Collections;
using Layer = Rhino.DocObjects.Layer;

namespace Speckle.Connectors.Rhino.HostApp;

/// <summary>
/// Utility class managing layer creation. Expects to be a scoped dependency per receive operation.
/// </summary>
public class RhinoLayerBaker : TraversalContextUnpacker
{
  private readonly RhinoMaterialBaker _materialBaker;
  private readonly RhinoColorBaker _colorBaker;
  private readonly Dictionary<string, int> _hostLayerCache = new();

  public RhinoLayerBaker(RhinoMaterialBaker materialBaker, RhinoColorBaker colorBaker)
  {
    _materialBaker = materialBaker;
    _colorBaker = colorBaker;
  }

  /// <summary>
  /// Creates the base layer and adds it to the cache.
  /// </summary>
  /// <param name="baseLayerName"></param>
  private void CreateBaseLayer(string baseLayerName)
  {
    var index = RhinoDoc.ActiveDoc.Layers.Add(new Layer { Name = baseLayerName }); // POC: too much effort right now to wrap around the interfaced layers and doc
    _hostLayerCache[baseLayerName] = index;
  }

  /// <summary>
  /// Creates all layers needed for receiving data.
  /// </summary>
  /// <param name="paths">Collections of paths</param>
  /// <param name="baseLayerName">Name of the base layer</param>
  /// <remarks>Make sure this is executing on the main thread, using e.g RhinoApp.InvokeAndWait.</remarks>
  public void CreateAllLayersForReceive(IEnumerable<Collection[]> paths, string baseLayerName)
  {
    CreateBaseLayer(baseLayerName);
    var uniquePaths = new Dictionary<string, Collection[]>();
    foreach (var path in paths)
    {
      var names = path.Select(o => string.IsNullOrWhiteSpace(o.name) ? "unnamed" : o.name);
      var key = string.Join(",", names!);
      uniquePaths[key] = path;
    }

    foreach (var uniquePath in uniquePaths)
    {
      var layerIndex = CreateLayerFromPath(uniquePath.Value, baseLayerName);
    }
  }

  /// <summary>
  /// Retrieves the index of a layer based on the given collection path and base layer name.
  /// </summary>
  /// <param name="collectionPath">The array containing the collection path to the layer.</param>
  /// <param name="baseLayerName">The name of the base layer.</param>
  /// <returns>The index of the layer in the cache.</returns>
  /// <exception cref="SpeckleException">Thrown when the layer is not found in the cache. This can happen if you didn't call previously <see cref="CreateAllLayersForReceive"/></exception>
  public int GetLayerIndex(Collection[] collectionPath, string baseLayerName)
  {
    var layerPath = collectionPath
      .Select(o => string.IsNullOrWhiteSpace(o.name) ? "unnamed" : o.name)
      .Prepend(baseLayerName);

    var layerFullName = string.Join(Layer.PathSeparator, layerPath);

    if (_hostLayerCache.TryGetValue(layerFullName, out int existingLayerIndex))
    {
      return existingLayerIndex;
    }

    throw new SpeckleException("Did not find a layer in the cache.");
  }

  /// <summary>
  /// Creates a layer based on the given collection path and adds it to the Rhino document.
  /// </summary>
  /// <param name="collectionPath">An array of Collection objects representing the path to create the layer.</param>
  /// <param name="baseLayerName">The base layer name to start creating the new layer.</param>
  /// <returns>The index of the last created layer.</returns>
  private int CreateLayerFromPath(Collection[] collectionPath, string baseLayerName)
  {
    var currentLayerName = baseLayerName;
    var currentDocument = RhinoDoc.ActiveDoc; // POC: too much effort right now to wrap around the interfaced layers
    Layer? previousLayer = currentDocument.Layers.FindName(currentLayerName);
    foreach (Collection collection in collectionPath)
    {
      currentLayerName += Layer.PathSeparator + collection.name;
      currentLayerName = currentLayerName.Replace("{", "").Replace("}", ""); // Rhino specific cleanup for gh (see RemoveInvalidRhinoChars)
      if (_hostLayerCache.TryGetValue(currentLayerName, out int value))
      {
        previousLayer = currentDocument.Layers.FindIndex(value);
        continue;
      }

      var cleanNewLayerName = collection.name.Replace("{", "").Replace("}", "");
      Layer newLayer = new() { Name = cleanNewLayerName, ParentLayerId = previousLayer?.Id ?? Guid.Empty };

      // set material
      if (
        _materialBaker.ObjectIdAndMaterialIndexMap.TryGetValue(
          collection.applicationId ?? collection.id,
          out int mIndex
        )
      )
      {
        newLayer.RenderMaterialIndex = mIndex;
      }

      // set color
      if (
        _colorBaker.ObjectColorsIdMap.TryGetValue(
          collection.applicationId ?? collection.id,
          out (Color, ObjectColorSource) color
        )
      )
      {
        newLayer.Color = color.Item1;
      }

      int index = currentDocument.Layers.Add(newLayer);
      _hostLayerCache.Add(currentLayerName, index);
      previousLayer = currentDocument.Layers.FindIndex(index); // note we need to get the correct id out, hence why we're double calling this
    }

    return previousLayer.Index;
  }
}
