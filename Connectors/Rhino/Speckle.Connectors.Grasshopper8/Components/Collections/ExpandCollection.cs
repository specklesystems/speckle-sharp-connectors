using System.Collections;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
// using Speckle.Connectors.Grasshopper8.Parameters;
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
    pManager.AddGenericParameter("Collection", "C", "Collection to unpack", GH_ParamAccess.item);
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager) { }

  protected override void SolveInstance(IGH_DataAccess da)
  {
    Collection res = new();
    da.GetData("Collection", ref res);
    var c = res;

    Name = c.name;
    NickName = c.name;

    var objects = c.elements.Where(el => el is not Collection).ToList();
    var collections = c.elements.Where(el => el is Collection).OfType<Collection>().ToList();

    var outputParams = new List<OutputParamWrapper>();
    if (objects.Count != 0)
    {
      var param = new Param_GenericObject()
      {
        Name = "Inner objects",
        NickName = "OBJS",
        Description =
          "Some collections may contain a mix of objects and other collections. Here we output the atomic objects from within this collection.",
        Access = GH_ParamAccess.list // NOTE: todo check on list if it's tree path-based
      };

      outputParams.Add(new OutputParamWrapper(param, objects));
    }

    foreach (var collection in collections)
    {
      // skip empty
      if (collection.elements.Count == 0)
      {
        continue;
      }

      var hasInnerCollections = collection.elements.Any(el => el is Collection);
      var isPathBasedCollection = collection["treePath"] as string; // Note: this is a reminder for the future
      var nickName = collection.name;
      if (collection.name.Length > 12)
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
      outputParams.Add(new OutputParamWrapper(param, hasInnerCollections ? collection : collection.elements));
    }

    if (da.Iteration == 0 && OutputMismatch2(outputParams))
    {
      OnPingDocument()
        .ScheduleSolution(
          5,
          _ =>
          {
            AutoCreateOutputs2(outputParams);
          }
        );
    }
    else
    {
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

  // public override void DrawViewportWires(IGH_PreviewArgs args) => base.DrawViewportWires(args);
  // public override void DrawViewportMeshes(IGH_PreviewArgs args) => base.DrawViewportMeshes(args);

  private bool OutputMismatch2(List<OutputParamWrapper> outputParams)
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

  public void AutoCreateOutputs2(List<OutputParamWrapper> outputParams)
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

public record OutputParamWrapper(Param_GenericObject Param, object Values);
