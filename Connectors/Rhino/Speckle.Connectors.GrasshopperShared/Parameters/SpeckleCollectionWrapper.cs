using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Connectors.GrasshopperShared.Components;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Properties;
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

  public override string ToString() => $"{Name} [{Elements.Count}]";

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
        case SpeckleObjectWrapper o:
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
            SpeckleObjectWrapper o => o.DeepCopy(),
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
      if (obj is SpeckleObjectWrapper so)
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

public partial class SpeckleCollectionWrapperGoo : GH_Goo<SpeckleCollectionWrapper> //, IGH_PreviewData // can be made previewable later
{
  public override IGH_Goo Duplicate() => new SpeckleCollectionWrapperGoo(Value.DeepCopy());

  public override string ToString() => $@"Speckle Collection Goo [{m_value.Name} ({Value.Elements.Count})]";

  public override bool IsValid => true;
  public override string TypeName => "Speckle collection wrapper";
  public override string TypeDescription => "Speckle collection wrapper";

  public override bool CastFrom(object source)
  {
    switch (source)
    {
      case SpeckleCollectionWrapper speckleGrasshopperCollection:
        Value = speckleGrasshopperCollection;
        return true;
      case GH_Goo<SpeckleCollectionWrapper> speckleGrasshopperCollectionGoo:
        Value = speckleGrasshopperCollectionGoo.Value;
        return true;
    }

    // Handle case of model objects in rhino 8
    return CastFromModelLayer(source);
  }

#if !RHINO8_OR_GREATER
  private bool CastFromModelLayer(object _) => false;

  private bool CastToModelLayer<T>(ref T _) => false;
#endif

  public override bool CastTo<T>(ref T target)
  {
    return CastToModelLayer(ref target);
  }

  public SpeckleCollectionWrapperGoo() { }

  public SpeckleCollectionWrapperGoo(SpeckleCollectionWrapper value)
  {
    Value = value;
  }
}

public class SpeckleCollectionParam : GH_Param<SpeckleCollectionWrapperGoo>, IGH_BakeAwareObject, IGH_PreviewObject
{
  public SpeckleCollectionParam()
    : this(GH_ParamAccess.item) { }

  public SpeckleCollectionParam(IGH_InstanceDescription tag)
    : base(tag) { }

  public SpeckleCollectionParam(IGH_InstanceDescription tag, GH_ParamAccess access)
    : base(tag, access) { }

  public SpeckleCollectionParam(GH_ParamAccess access)
    : base(
      "Speckle Collection",
      "SCO",
      "A Speckle collection, corresponding to layers in Rhino",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.PARAMETERS,
      access
    ) { }

  public override Guid ComponentGuid => new("6E871D5B-B221-4992-882A-EFE6796F3010");
  protected override Bitmap Icon => Resources.speckle_param_collection;

  bool IGH_BakeAwareObject.IsBakeCapable => // False if no data
    !VolatileData.IsEmpty;

  void IGH_BakeAwareObject.BakeGeometry(RhinoDoc doc, List<Guid> objIds)
  {
    // Iterate over all data stored in the parameter
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleCollectionWrapperGoo goo)
      {
        goo.Value.Bake(doc, objIds, true);
      }
    }
  }

  void IGH_BakeAwareObject.BakeGeometry(RhinoDoc doc, ObjectAttributes att, List<Guid> objIds)
  {
    // Iterate over all data stored in the parameter
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleCollectionWrapperGoo goo)
      {
        goo.Value.Bake(doc, objIds, true);
      }
    }
  }

  private BoundingBox _clippingBox;
  public BoundingBox ClippingBox => _clippingBox;

  bool IGH_PreviewObject.Hidden { get; set; }

  public bool IsPreviewCapable => !VolatileData.IsEmpty;

  private List<SpeckleObjectWrapper> _previewObjects = new();

  public void DrawViewportMeshes(IGH_PreviewArgs args)
  {
    _previewObjects = new();
    _clippingBox = new();

    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleCollectionWrapperGoo goo)
      {
        FlattenForPreview(goo.Value);
      }
    }

    if (_previewObjects.Count == 0)
    {
      return;
    }

    var isSelected = args.Document.SelectedObjects().Contains(this);
    foreach (var elem in _previewObjects)
    {
      elem.DrawPreview(args, isSelected);
    }
  }

  public void DrawViewportWires(IGH_PreviewArgs args)
  {
    // todo?
  }

  private void FlattenForPreview(SpeckleCollectionWrapper collWrapper)
  {
    foreach (var element in collWrapper.Elements)
    {
      if (element is SpeckleCollectionWrapper subCollWrapper)
      {
        FlattenForPreview(subCollWrapper);
      }

      if (element is SpeckleObjectWrapper objWrapper)
      {
        _previewObjects.Add(objWrapper);
        var box = objWrapper.GeometryBase is null ? new() : objWrapper.GeometryBase.GetBoundingBox(false);
        _clippingBox.Union(box);
      }
    }
  }
}
