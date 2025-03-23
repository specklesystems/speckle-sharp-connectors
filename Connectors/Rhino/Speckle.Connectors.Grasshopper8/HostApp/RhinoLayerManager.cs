using Rhino;
using Rhino.DocObjects;

namespace Speckle.Connectors.Grasshopper8.HostApp;

/// <summary>
/// Utility class managing layer creation.
/// </summary>
public class RhinoLayerManager
{
  public bool LayerExists(RhinoDoc doc, string fullPath, out int layerIndex)
  {
    layerIndex = doc.Layers.FindByFullPath(fullPath, -1);
    return layerIndex != -1;
  }

  private int CreateLayer(RhinoDoc doc, string name, Guid parentId)
  {
    Layer layer = new() { Name = name, ParentLayerId = parentId };
    return doc.Layers.Add(layer);
  }

  public int CreateLayerByFullPath(RhinoDoc doc, string fullPath)
  {
    if (string.IsNullOrWhiteSpace(fullPath) || doc == null)
    {
      return -1;
    }

    string[] layerParts = fullPath.Split(["::"], StringSplitOptions.RemoveEmptyEntries);
    int parentLayerIndex = -1;
    string currentfullpath = layerParts.First();
    foreach (string layerName in layerParts)
    {
      // Find or create the layer at this level
      Guid currentLayerId = Guid.Empty;
      if (LayerExists(doc, currentfullpath, out int currentLayerIndex))
      {
        currentLayerId = doc.Layers.FindIndex(currentLayerIndex).Id;
      }
      else
      {
        currentLayerIndex = CreateLayer(doc, layerName, currentLayerId);
      }
      parentLayerIndex = currentLayerIndex;
    }

    return parentLayerIndex;
  }
}
