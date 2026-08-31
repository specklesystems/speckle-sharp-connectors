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

  public List<ISpeckleCollectionObject?> Elements { get; set; } = new();

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
  /// Scans all dynamic list-of-Base properties on a root object and builds a single
  /// <see cref="SpecklePropertyGroupGoo"/> keyed by proxy type name (e.g. "levelProxies",
  /// "analysisResults"). Each value is a list of property groups, one per proxy instance.
  /// Returns null when no proxy lists are found.
  /// </summary>
  public static SpecklePropertyGroupGoo? BuildProxiesGoo(Base root)
  {
    var result = new Dictionary<string, ISpecklePropertyGoo>();
    foreach (var kvp in root.GetMembers(DynamicBaseMemberType.Dynamic))
    {
      if (kvp.Value is not List<object> list)
      {
        continue;
      }

      var proxies = list.OfType<Base>().ToList();
      if (proxies.Count == 0)
      {
        continue;
      }

      var proxyList = proxies.Select(b => (object)ProxyToPropertyGroup(b)).ToList();
      result[kvp.Key] = new SpecklePropertyGoo { Value = proxyList };
    }

    return result.Count > 0 ? new SpecklePropertyGroupGoo(result) : null;
  }

  private static SpecklePropertyGroupGoo ProxyToPropertyGroup(Base proxy) => new(BaseToPropDict(proxy));

  private static Dictionary<string, object?> BaseToPropDict(Base b)
  {
    var dict = new Dictionary<string, object?>();
    foreach (var kvp in b.GetMembers())
    {
      if (kvp.Key == nameof(Base.DynamicPropertyKeys))
      {
        continue;
      }

      dict[kvp.Key] = ConvertProxyValue(kvp.Value);
    }
    return dict;
  }

  private static object? ConvertProxyValue(object? value) =>
    value switch
    {
      Base b => BaseToPropDict(b),
      List<object> list => list.Select(ConvertProxyValue).ToList(),
      _ => value,
    };

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
        case null:
          continue; // skip nulls (CNX-2855)
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

  /// <summary>Assigns <paramref name="context"/> to this collection and everything beneath it.</summary>
  /// <remarks>
  /// Definitions are only reached one level deep, through the instances referencing them. Only the legacy path relies
  /// on this walk and there's no bundle to query there; the artefact builder stamps definitions from its own map.
  /// </remarks>
  public void SetModelContext(SpeckleModelContext? context)
  {
    ModelContext = context;
    foreach (var element in Elements)
    {
      switch (element)
      {
        case SpeckleCollectionWrapper child:
          child.SetModelContext(context);
          break;
        case SpeckleBlockInstanceWrapper instance:
          instance.ModelContext = context;
          StampDefinition(instance.Definition, context);
          break;
        case SpeckleDataObjectWrapper dataObject:
          dataObject.ModelContext = context;
          foreach (var geometry in dataObject.Geometries)
          {
            geometry.ModelContext = context;
          }
          break;
        case SpeckleWrapper wrapper:
          wrapper.ModelContext = context;
          break;
        default:
          break;
      }
    }
  }

  private static void StampDefinition(SpeckleBlockDefinitionWrapper? definition, SpeckleModelContext? context)
  {
    if (definition is null)
    {
      return;
    }
    definition.ModelContext = context;
    foreach (var member in definition.Objects)
    {
      member.ModelContext = context;
    }
  }

  public SpeckleCollectionWrapper DeepCopy() =>
    new()
    {
      Base = new Collection
      {
        name = Collection.name,
        applicationId = Collection.applicationId,
        id = Collection.id,
      },
      Color = Color,
      Material = Material,
      ApplicationId = ApplicationId,
      Name = Name,
      Path = Path,
      Topology = Topology,
      ModelContext = ModelContext,
      Elements = Elements
        .Select(e =>
          e switch
          {
            null => null, // preserve nulls (CNX-2855)
            SpeckleCollectionWrapper c => c.DeepCopy(),
            SpeckleBlockInstanceWrapper b => b.DeepCopy(),
            SpeckleGeometryWrapper o => o.DeepCopy(),
            _ => e,
          }
        )
        .ToList(),
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
      if (obj is null)
      {
        continue; // skip nulls (CNX-2855)
      }

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
