using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
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
    Collection? c = new();
    da.GetData("Collection", ref c);

    Name = c.name;
    NickName = c.name;

    var objects = c.elements.Where(el => el is not Collection).ToList();
    var collections = c.elements.Where(el => el is Collection).OfType<Collection>().ToList();
    var hasObjects = objects.Count != 0;

    var outputPortNames = new List<string>();
    if (objects.Count != 0)
    {
      outputPortNames.Add("objects");
    }

    foreach (var collection in collections)
    {
      outputPortNames.Add(collection.name);
    }

    if (da.Iteration == 0 && Params.Output.Count != collections.Count + (hasObjects ? 1 : 0))
    {
      OnPingDocument()
        .ScheduleSolution(
          5,
          _ =>
          {
            AutoCreateOutputs(outputPortNames);
          }
        );
    }

    if (Params.Output.Count != collections.Count + (hasObjects ? 1 : 0))
    {
      return;
    }

    var padIfHasObjects = hasObjects ? 1 : 0;

    if (hasObjects)
    {
      da.SetDataList(0, objects);
    }

    for (int i = 0; i < collections.Count; i++)
    {
      da.SetData(i + padIfHasObjects, collections[i]);
    }
  }

  private List<Param_GenericObject> _myParams = new();

  public void AutoCreateOutputs(List<string> portNames)
  {
    while (Params.Output.Count > 0)
    {
      Params.UnregisterOutputParameter(Params.Output[^1]);
    }

    int count = 0;
    _myParams = new();
    foreach (var portName in portNames)
    {
      var param = new Param_GenericObject
      {
        Name = portName,
        NickName = portName,
        MutableNickName = false,
        Access = count == 0 ? GH_ParamAccess.list : GH_ParamAccess.item, // TODO: objects should always be a list or a tree, depending on whether the collection is a gh collection with a path prop
      };
      _myParams.Add(param);
      Params.RegisterOutputParam(param);
      count++;
    }

    Params.OnParametersChanged();
    // VariableParameterMaintenance();
    ExpireSolution(false);
  }

  public void VariableParameterMaintenance()
  {
    int count = 0;
    foreach (var param in _myParams)
    {
      Params.Output[count].Access = param.Access;
      Params.Output[count].Name = param.Name;
      Params.Output[count].MutableNickName = false;
      count++;
    }
  }

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
