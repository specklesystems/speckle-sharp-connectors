using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
using Speckle.Connectors.Grasshopper8.HostApp;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.Grasshopper8.Parameters;

// public class SpeckleCollectionWrapper : Base
// {
//   public Collection OriginalObject { get; set; }
//
//   public override string ToString() => $"{OriginalObject.name} [{OriginalObject.elements.Count}]";
// }

public class SpeckleCollectionGoo : GH_Goo<Collection>, ISpeckleGoo //, IGH_PreviewData // can be made previewable later
{
  public override IGH_Goo Duplicate() => throw new NotImplementedException();

  public override string ToString() => $"{Value.name} ({Value.elements.Count})";

  // TODO: this would be *sooo* much easier if all speckle collections in grasshopper are changed to collection goos.
  // collection goo can even contain a formalized full path string, and a ref to its parent goo.
  // all collection bake methods can now be contained on the collection goo as well, instead of moved out to Rhino layer Manager.
  // which also means they would be callable from object goo bakes as well.
  public void Bake(
    RhinoDoc doc,
    List<Guid> obj_ids,
    string parentPath,
    bool bakeObjects,
    string? name = null,
    List<Sdk.Models.Base>? elements = null
  )
  {
    var layerManager = new RhinoLayerManager();

    string fullPath = $"{parentPath}::{name ?? Value.name}";
    if (layerManager.LayerExists(doc, fullPath, out int currentLayerIndex))
    {
      parentPath = fullPath;
    }
    else
    {
      currentLayerIndex = layerManager.CreateLayerByFullPath(doc, fullPath);
    }

    // then bake elements in this collection
    List<Sdk.Models.Base> e = elements ?? Value.elements;
    foreach (var obj in e)
    {
      if (obj is SpeckleObject so)
      {
        if (bakeObjects)
        {
          so.Bake(doc, obj_ids, currentLayerIndex);
        }
      }
      else if (obj is Collection c)
      {
        Bake(doc, obj_ids, parentPath, bakeObjects, c.name, c.elements);
      }
    }
  }

  public override bool IsValid => true;
  public override string TypeName => "Speckle collection wrapper";
  public override string TypeDescription => "Speckle collection wrapper";

  public SpeckleCollectionGoo() { }

  public SpeckleCollectionGoo(Collection value)
  {
    Value = value;
  }
}

public class SpeckleCollectionWrapperParam : GH_Param<SpeckleCollectionGoo>, IGH_BakeAwareObject
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
        goo.Bake(doc, obj_ids, "", true, null, null);
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
        goo.Bake(doc, obj_ids, "", true, null, null);
      }
    }
  }
}
