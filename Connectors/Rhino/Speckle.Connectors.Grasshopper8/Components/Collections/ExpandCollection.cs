using System.Collections;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Rhino.Geometry;
using Speckle.Connectors.Grasshopper8.Parameters;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.Grasshopper8.Components.Collections;

#pragma warning disable CA1711
public class ExpandCollection : GH_Component, IGH_VariableParameterComponent
#pragma warning restore CA1711
{
  public override Guid ComponentGuid => new("69BC8CFB-A72F-4A83-9263-F3399DDA2E5E");

  public ExpandCollection()
    : base("Expand Collection", "expand", "Expands a new collection", "Speckle", "Collections") { }

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddParameter(
      new SpeckleCollectionWrapperParam(GH_ParamAccess.item),
      "Data",
      "D",
      "The data you want to expand",
      GH_ParamAccess.item
    );
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager) { }

  private List<SpeckleObject> _previewObjects = new();

  protected override void SolveInstance(IGH_DataAccess da)
  {
    SpeckleCollectionGoo res = new();
    da.GetData(0, ref res);
    var c = res.Value;

    Name = c.name;
    NickName = c.name;

    var objects = c
      .elements.Where(el => el is not Collection)
      .OfType<SpeckleObject>()
      .Select(o => new SpeckleObjectGoo(o))
      .ToList();
    var collections = c.elements.Where(el => el is Collection).OfType<Collection>().ToList();

    var outputParams = new List<OutputParamWrapper>();
    if (objects.Count != 0)
    {
      var param = new Param_GenericObject()
      {
        Name = "Inner objects",
        NickName = "Inner Objs",
        Description =
          "Some collections may contain a mix of objects and other collections. Here we output the atomic objects from within this collection.",
        Access = GH_ParamAccess.list // NOTE: todo check on list if it's tree path-based
      };

      outputParams.Add(new OutputParamWrapper(param, objects, false));
    }

    foreach (var collection in collections)
    {
      // skip empty
      if (collection.elements.Count == 0)
      {
        continue;
      }

      var hasInnerCollections = collection.elements.Any(el => el is Collection);
      var isPathBasedCollection = collection["path"] as string; // Note: this is a reminder for the future
      var nickName = collection.name;
      if (collection.name.Length > 16)
      {
        nickName = collection.name[..3];
        nickName += "..." + collection.name[^3..];
      }

      var param = new Param_GenericObject()
      {
        Name = collection.name,
        NickName = nickName,
        Access = hasInnerCollections ? GH_ParamAccess.item : GH_ParamAccess.list // we will directly set objects out; note access can be list or tree based on whether it will be a path based collection
      };
      if (!hasInnerCollections)
      {
        _previewObjects.AddRange(collection.elements.Cast<SpeckleObject>());
      }

      outputParams.Add(
        new OutputParamWrapper(
          param,
          hasInnerCollections
            ? new SpeckleCollectionGoo(collection)
            : collection.elements.OfType<SpeckleObject>().Select(o => new SpeckleObjectGoo(o)).ToList(),
          hasInnerCollections
        )
      );
    }

    if (da.Iteration == 0 && OutputMismatch(outputParams))
    {
      OnPingDocument()
        .ScheduleSolution(
          5,
          _ =>
          {
            CreateOutputs(outputParams);
          }
        );
    }
    else
    {
      _previewObjects = new();

      FlattenForPreview(c);
      for (int i = 0; i < outputParams.Count; i++)
      {
        var outParam = Params.Output[i];
        switch (outParam.Access)
        {
          case GH_ParamAccess.item:
            da.SetData(i, outputParams[i].Values);
            break;
          case GH_ParamAccess.list:
            da.SetDataList(i, outputParams[i].Values as IList);
            break;
          case GH_ParamAccess.tree:
            //TODO: means we need to convert the collection to a tree
            break;
        }
      }
    }
  }

  private BoundingBox _clippingBox;
  public override BoundingBox ClippingBox => _clippingBox;

  private void FlattenForPreview(Collection c)
  {
    _clippingBox = new BoundingBox();
    foreach (var element in c.elements)
    {
      if (element is Collection subCol)
      {
        FlattenForPreview(subCol);
      }

      if (element is SpeckleObject sg)
      {
        _previewObjects.Add(sg);
        var box = sg.GeometryBase.GetBoundingBox(false);
        _clippingBox.Union(box);
      }
    }
  }

  // public override void DrawViewportWires(IGH_PreviewArgs args) => base.DrawViewportWires(args);
  public override void DrawViewportMeshes(IGH_PreviewArgs args)
  {
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

  private bool OutputMismatch(List<OutputParamWrapper> outputParams)
  {
    if (Params.Output.Count != outputParams.Count)
    {
      return true;
    }

    var count = 0;
    foreach (var newParam in outputParams)
    {
      var oldParam = Params.Output[count];
      if (
        oldParam.NickName != newParam.Param.NickName
        || oldParam.Name != newParam.Param.Name
        || oldParam.Access != newParam.Param.Access
      )
      {
        return true;
      }
      count++;
    }

    return false;
  }

  private void CreateOutputs(List<OutputParamWrapper> outputParams)
  {
    while (Params.Output.Count > 0)
    {
      Params.UnregisterOutputParameter(Params.Output[^1]);
    }

    foreach (var newParam in outputParams)
    {
      var param = new Param_GenericObject
      {
        Name = newParam.Param.Name,
        NickName = newParam.Param.NickName,
        MutableNickName = false,
        Access = newParam.Param.Access // count == 0 ? GH_ParamAccess.list : GH_ParamAccess.item, // TODO: objects should always be a list or a tree, depending on whether the collection is a gh collection with a path prop
      };
      Params.RegisterOutputParam(param);
    }

    Params.OnParametersChanged();
    VariableParameterMaintenance();
    ExpireSolution(false);
  }

  public void VariableParameterMaintenance() { }

  public bool CanInsertParameter(GH_ParameterSide side, int index) => false;

  public bool CanRemoveParameter(GH_ParameterSide side, int index) => false;

  public IGH_Param CreateParameter(GH_ParameterSide side, int index)
  {
    var myParam = new Param_GenericObject
    {
      Name = GH_ComponentParamServer.InventUniqueNickname("ABCD", Params.Input),
      MutableNickName = true,
      Optional = true
    };
    myParam.NickName = myParam.Name;
    return myParam;
  }

  public bool DestroyParameter(GH_ParameterSide side, int index) => side == GH_ParameterSide.Output;
}

public record OutputParamWrapper(Param_GenericObject Param, object Values, bool IsCollection);
