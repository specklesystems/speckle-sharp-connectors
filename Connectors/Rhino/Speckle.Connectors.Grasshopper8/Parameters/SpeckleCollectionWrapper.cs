using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Grasshopper.Rhinoceros;
using Grasshopper.Rhinoceros.Model;
using Rhino;
using Rhino.DocObjects;
using Speckle.Connectors.Grasshopper8.HostApp;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Layer = Rhino.DocObjects.Layer;

namespace Speckle.Connectors.Grasshopper8.Parameters;

#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
public class SpeckleCollection : Base
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
{
  // the original collection
  public Collection Collection { get; set; }

  // the list of layer names that build up the path to this collection, including this collection name
  public ObservableCollection<string> Path { get; set; }

  public string Topology { get; set; }

  public int? Color { get; set; }

  public override string ToString() => $"{Collection.name} [{Collection.elements.Count}]";

  public SpeckleCollection(Collection value, List<string> path, int? color)
  {
    Collection = value;
    Path = new ObservableCollection<string>(path);
    Color = color;

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
      if (element is SpeckleObject o)
      {
        o.Path = newPath;
      }
      else if (element is SpeckleCollection c)
      {
        c.Path = new ObservableCollection<string>(newPath);
      }
    }
  }

  public void Bake(
    RhinoDoc doc,
    List<Guid> obj_ids,
    List<string> path,
    bool bakeObjects,
    List<Sdk.Models.Base>? elements = null
  )
  {
    if (!LayerExists(doc, path, out int currentLayerIndex))
    {
      currentLayerIndex = CreateLayerByPath(doc, path);
    }

    // then bake elements in this collection
    List<Sdk.Models.Base> e = elements ?? Collection.elements;
    foreach (var obj in e)
    {
      if (obj is SpeckleObject so)
      {
        if (bakeObjects)
        {
          so.Bake(doc, obj_ids, currentLayerIndex, true);
        }
      }
      else if (obj is SpeckleCollection c)
      {
        path.Add(c.Collection.name);
        Bake(doc, obj_ids, path, bakeObjects, c.Collection.elements);
      }
    }
  }

  private bool LayerExists(RhinoDoc doc, List<string> path, out int layerIndex)
  {
    var fullPath = string.Join("::", path);
    layerIndex = doc.Layers.FindByFullPath(fullPath, -1);
    return layerIndex != -1;
  }

  private int CreateLayer(RhinoDoc doc, string name, Guid parentId)
  {
    Layer layer = new() { Name = name, ParentLayerId = parentId };
    return doc.Layers.Add(layer);
  }

  public int CreateLayerByPath(RhinoDoc doc, List<string> path)
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
        currentLayerIndex = CreateLayer(doc, layerName, currentLayerId);
        currentLayerId = doc.Layers.FindIndex(currentLayerIndex).Id;
      }
      parentLayerIndex = currentLayerIndex;
    }

    return parentLayerIndex;
  }
}

public class SpeckleCollectionGoo : GH_Goo<SpeckleCollection>, ISpeckleGoo //, IGH_PreviewData // can be made previewable later
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
      case SpeckleCollection speckleGrasshopperCollection:
        Value = speckleGrasshopperCollection;
        return true;
      case GH_Goo<SpeckleCollection> speckleGrasshopperCollectionGoo:
        Value = speckleGrasshopperCollectionGoo.Value;
        return true;
      case ModelLayer modelLayer:
        Collection modelCollection = new() { name = modelLayer.Name, elements = new() };
        Value = new SpeckleCollection(
          modelCollection,
          GetModelLayerPath(modelLayer),
          modelLayer.DisplayColor?.ToArgb()
        );
        return true;
    }

    return false;
  }

  private List<string> GetModelLayerPath(ModelLayer modellayer)
  {
    ModelContentName currentParent = modellayer.Parent;
    ModelContentName stem = modellayer.Parent.Stem;
    List<string> path = new() { modellayer.Name };
    while (currentParent != stem)
    {
      path.Add(currentParent);
      currentParent = currentParent.Parent;
    }
    path.Add(stem);

    path.Reverse();
    return path;
  }

  public SpeckleCollectionGoo() { }

  public SpeckleCollectionGoo(SpeckleCollection value)
  {
    Value = value;
  }
}

public class SpeckleCollectionParam : GH_Param<SpeckleCollectionGoo>, IGH_BakeAwareObject
{
  public SpeckleCollectionParam()
    : this(GH_ParamAccess.item) { }

  public SpeckleCollectionParam(IGH_InstanceDescription tag)
    : base(tag) { }

  public SpeckleCollectionParam(IGH_InstanceDescription tag, GH_ParamAccess access)
    : base(tag, access) { }

  public SpeckleCollectionParam(GH_ParamAccess access)
    : base("Speckle Collection Wrapper", "SCO", "XXXXX", "Speckle", "Params", access) { }

  public override Guid ComponentGuid => new("6E871D5B-B221-4992-882A-EFE6796F3010");
  protected override Bitmap Icon => BitmapBuilder.CreateHexagonalBitmap("C");

  bool IGH_BakeAwareObject.IsBakeCapable => // False if no data
    !VolatileData.IsEmpty;

  void IGH_BakeAwareObject.BakeGeometry(RhinoDoc doc, List<Guid> obj_ids)
  {
    // Iterate over all data stored in the parameter
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleCollectionGoo goo)
      {
        goo.Value.Bake(doc, obj_ids, goo.Value.Path.ToList(), true, goo.Value.Collection.elements);
      }
    }
  }

  void IGH_BakeAwareObject.BakeGeometry(RhinoDoc doc, ObjectAttributes att, List<Guid> obj_ids)
  {
    // Iterate over all data stored in the parameter
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleCollectionGoo goo)
      {
        goo.Value.Bake(doc, obj_ids, goo.Value.Path.ToList(), true, goo.Value.Collection.elements);
      }
    }
  }
}
