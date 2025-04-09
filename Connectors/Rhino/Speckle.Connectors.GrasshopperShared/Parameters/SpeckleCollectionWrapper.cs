using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Connectors.GrasshopperShared.Components;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Layer = Rhino.DocObjects.Layer;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
public class SpeckleCollectionWrapper : Base
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
{
  // the original collection
  public Collection Collection { get; set; }

  // the list of layer names that build up the path to this collection, including this collection name
  public ObservableCollection<string> Path { get; set; }

  public string Topology { get; set; }

  public Color? Color { get; set; }

  public override string ToString() => $"{Collection.name} [{Collection.elements.Count}]";

  public SpeckleCollectionWrapper(Collection value, List<string> path, Color? color)
  {
    Collection = value;
    Path = new ObservableCollection<string>(path);
    Color = color;
    applicationId = value.applicationId;

    // add listener on path changing.
    // this can be triggered by a create collection node, that changes the path of this collection.
    // when this happens, we want to update the paths of all elements downstream
    Path.CollectionChanged += OnPathChanged;
  }

  private void OnPathChanged(object sender, NotifyCollectionChangedEventArgs e)
  {
    var newPath = e.NewItems.Cast<string>().ToList();
    foreach (var element in Collection.elements)
    {
      if (element is SpeckleObjectWrapper o)
      {
        o.Path = newPath;
      }
      else if (element is SpeckleCollectionWrapper c)
      {
        c.Path = new ObservableCollection<string>(newPath);
      }
    }
  }

  /// <summary>
  /// Bakes this collection as a layer, in its path structure.
  /// </summary>
  /// <param name="doc"></param>
  /// <param name="obj_ids"></param>
  /// <param name="bakeObjects"></param>
  /// <returns>The index of the baked layer</returns>
  public int Bake(RhinoDoc doc, List<Guid> obj_ids, bool bakeObjects, int parentLayerIndex = -1)
  {
    var path = Path.ToList();
    if (!LayerExists(doc, path, out int currentLayerIndex))
    {
      if (parentLayerIndex != -1)
      {
        Guid parentLayerId = doc.Layers[parentLayerIndex].Id;
        currentLayerIndex = CreateLayer(doc, Collection.name, parentLayerId, Color);
        Guid currentLayerId = doc.Layers.FindIndex(currentLayerIndex).Id;
        obj_ids.Add(currentLayerId);
      }
      else
      {
        currentLayerIndex = CreateLayerByPath(doc, path, Color, obj_ids);
      }
    }

    // then bake elements in this collection
    foreach (var obj in Collection.elements)
    {
      if (obj is SpeckleObjectWrapper so)
      {
        if (bakeObjects)
        {
          so.Bake(doc, obj_ids, currentLayerIndex, true);
        }
      }
      else if (obj is SpeckleCollectionWrapper c)
      {
        c.Bake(doc, obj_ids, bakeObjects, currentLayerIndex);
      }
    }

    return currentLayerIndex;
  }

  private bool LayerExists(RhinoDoc doc, List<string> path, out int layerIndex)
  {
    var fullPath = string.Join(Constants.LAYER_PATH_DELIMITER, path);
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

  private int CreateLayerByPath(RhinoDoc doc, List<string> path, Color? color, List<Guid> obj_ids)
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
        obj_ids.Add(currentLayerId);
      }

      parentLayerIndex = currentLayerIndex;
    }

    return parentLayerIndex;
  }
}

public partial class SpeckleCollectionWrapperGoo : GH_Goo<SpeckleCollectionWrapper>, ISpeckleGoo //, IGH_PreviewData // can be made previewable later
{
  public override IGH_Goo Duplicate() => throw new NotImplementedException();

  public override string ToString() =>
    $@"Speckle Collection Goo [{m_value.Collection?.name} ({Value.Collection.elements.Count})]";

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
    return HandleModelObjects(source);
  }

#if !RHINO8_OR_GREATER
  private bool HandleModelObjects(object _) => false;
#endif

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
      "XXXXX",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.PARAMETERS,
      access
    ) { }

  public override Guid ComponentGuid => new("6E871D5B-B221-4992-882A-EFE6796F3010");
  protected override Bitmap Icon => BitmapBuilder.CreateHexagonalBitmap("C");

  bool IGH_BakeAwareObject.IsBakeCapable => // False if no data
    !VolatileData.IsEmpty;

  void IGH_BakeAwareObject.BakeGeometry(RhinoDoc doc, List<Guid> obj_ids)
  {
    // Iterate over all data stored in the parameter
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleCollectionWrapperGoo goo)
      {
        goo.Value.Bake(doc, obj_ids, true);
      }
    }
  }

  void IGH_BakeAwareObject.BakeGeometry(RhinoDoc doc, ObjectAttributes att, List<Guid> obj_ids)
  {
    // Iterate over all data stored in the parameter
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleCollectionWrapperGoo goo)
      {
        goo.Value.Bake(doc, obj_ids, true);
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
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleCollectionWrapperGoo goo)
      {
        FlattenForPreview(goo.Value.Collection);
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

  private void FlattenForPreview(Collection c)
  {
    _clippingBox = new BoundingBox();
    _previewObjects = new();
    foreach (var element in c.elements)
    {
      if (element is SpeckleCollectionWrapper subCol)
      {
        FlattenForPreview(subCol.Collection);
      }

      if (element is SpeckleObjectWrapper sg)
      {
        _previewObjects.Add(sg);
        var box = sg.GeometryBase.GetBoundingBox(false);
        _clippingBox.Union(box);
      }
    }
  }
}
