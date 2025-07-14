using Grasshopper.Kernel.Types;
using Rhino;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Layer = Rhino.DocObjects.Layer;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

/// <summary>
/// A Wrapper class representing a Speckle Collection to Rhino Layer relationship.
/// </summary>
/// <remarks>
/// When constructing, the following properties need to be set in order:
/// <see cref="SpeckleWrapper.Base"/>, then <see cref="SpeckleWrapper.Name"/> and <see cref="SpeckleWrapper.ApplicationId"/>
/// This is because changing the Name or ApplicationId will update Collection.
/// </remarks>
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
public class SpeckleCollectionWrapper : SpeckleWrapper, ISpeckleCollectionObject
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
{
  public override required Base Base
  {
    get => Collection;
    set
    {
      if (value is not Collection coll)
      {
        throw new ArgumentException("Cannot create collection wrapper from a non-Collection Base");
      }

      Collection = coll;
    }
  }

  public Collection Collection { get; set; }

  private List<string> StoredPath { get; set; }

  /// <summary>
  /// List of Collection names that build up the path to this collection (inclusive of <see cref="SpeckleWrapper.Name"/>;
  /// </summary>
  /// <remarks>Setting this property will update all element paths inside <see cref="Elements"/></remarks>
  public required List<string> Path
  {
    get => StoredPath;
    set
    {
      StoredPath = value;
      OnPathChanged();
    }
  }

  public List<ISpeckleCollectionObject> Elements { get; set; } = new();

  /// <summary>
  /// The Grasshopper Topology of this collection. This setter also sets the "topology" prop dynamically on <see cref="Collection"/>
  /// </summary>
  public string? Topology
  {
    get => Collection[Constants.TOPOLOGY_PROP] as string;
    set => Collection[Constants.TOPOLOGY_PROP] = value;
  }

  /// <summary>
  /// The color of the <see cref="Base"/>
  /// </summary>
  public required Color? Color { get; set; }

  /// <summary>
  /// The material of the <see cref="Base"/>
  /// </summary>
  public required SpeckleMaterialWrapper? Material { get; set; }

  public override string ToString() => $"Speckle Collection : {Name} ({Elements.Count})";

  public override IGH_Goo CreateGoo() => new SpeckleCollectionWrapperGoo(this);

  /// <summary>
  /// Will attempt to retrieve an existing Layer from the <see cref="Path"/>.
  /// </summary>
  /// <returns>Index of existing layer if found, or -1 if not.</returns>
  public int GetLayerIndex() => RhinoDoc.ActiveDoc.Layers.FindByFullPath(string.Join("::", Path), -1);

  // updates the elements' paths inside this collection
  private void OnPathChanged()
  {
    var newPath = StoredPath.ToList();

    // then update paths and parents of all children
    foreach (var element in Elements)
    {
      switch (element)
      {
        case SpeckleGeometryWrapper o:
          o.Path = newPath;
          o.Parent = this;
          break;
        case SpeckleCollectionWrapper c:
          // don't forget to add the child collection name to the path
          var childPath = newPath.ToList();
          childPath.Add(c.Name);
          c.Path = childPath;
          break;
      }
    }
  }

  public SpeckleCollectionWrapper DeepCopy() =>
    new()
    {
      Base = new Collection(Collection.name) { applicationId = Collection.applicationId, id = Collection.id },
      Color = Color,
      Material = Material,
      ApplicationId = ApplicationId,
      Name = Name,
      Path = Path,
      Topology = Topology,
      Elements = Elements
        .Select(e =>
          e switch
          {
            SpeckleCollectionWrapper c => c.DeepCopy(),
            SpeckleBlockInstanceWrapper b => b.DeepCopy(),
            SpeckleGeometryWrapper o => o.DeepCopy(),
            _ => e
          }
        )
        .ToList()
    };

  /// <summary>
  /// Bakes this collection as a layer, in its path structure.
  /// </summary>
  /// <param name="doc"></param>
  /// <param name="objIds"></param>
  /// <param name="bakeObjects"></param>
  /// <returns>The index of the baked layer</returns>
  public int Bake(RhinoDoc doc, List<Guid> objIds, bool bakeObjects, int parentLayerIndex = -1)
  {
    if (!LayerExists(doc, Path, out int currentLayerIndex))
    {
      if (parentLayerIndex != -1)
      {
        Guid parentLayerId = doc.Layers[parentLayerIndex].Id;
        currentLayerIndex = CreateLayer(doc, Collection.name, parentLayerId, Color);
        Guid currentLayerId = doc.Layers.FindIndex(currentLayerIndex).Id;
        objIds.Add(currentLayerId);
      }
      else
      {
        currentLayerIndex = CreateLayerByPath(doc, Path, Color, objIds);
      }
    }

    // then bake elements in this collection
    foreach (var obj in Elements)
    {
      if (obj is SpeckleGeometryWrapper so)
      {
        if (bakeObjects)
        {
          so.Bake(doc, objIds, currentLayerIndex, true);
        }
      }
      else if (obj is SpeckleCollectionWrapper c)
      {
        c.Bake(doc, objIds, bakeObjects, currentLayerIndex);
      }
    }

    return currentLayerIndex;
  }

  private bool LayerExists(RhinoDoc doc, List<string> path, out int layerIndex)
  {
    var fullPath = string.Join("::", path);
    layerIndex = doc.Layers.FindByFullPath(fullPath, -1);
    return layerIndex != -1;
  }

  private int CreateLayer(RhinoDoc doc, string name, Guid parentId, Color? color)
  {
    Layer layer = new() { Name = name, ParentLayerId = parentId };
    if (color is not null)
    {
      layer.Color = color.Value;
    }

    return doc.Layers.Add(layer);
  }

  private int CreateLayerByPath(RhinoDoc doc, List<string> path, Color? color, List<Guid> objIds)
  {
    if (path.Count == 0 || doc == null)
    {
      return -1;
    }

    int parentLayerIndex = -1;
    List<string> currentfullpath = new();
    Guid currentLayerId = Guid.Empty;
    foreach (string layerName in path)
    {
      currentfullpath.Add(layerName);

      // Find or create the layer at this level
      if (LayerExists(doc, currentfullpath, out int currentLayerIndex))
      {
        currentLayerId = doc.Layers.FindIndex(currentLayerIndex).Id;
      }
      else
      {
        currentLayerIndex = CreateLayer(doc, layerName, currentLayerId, color);
        currentLayerId = doc.Layers.FindIndex(currentLayerIndex).Id;
        objIds.Add(currentLayerId);
      }

      parentLayerIndex = currentLayerIndex;
    }

    return parentLayerIndex;
  }
}
