using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Speckle.Connectors.Grasshopper8.Parameters;

namespace Speckle.Connectors.Grasshopper8.Components.Properties;

public class FilterPropertiesByPropertyGroupPaths : GH_Component, IGH_VariableParameterComponent
{
  /// <summary>
  /// Gets the unique ID for this component. Do not change this ID after release.
  /// </summary>
  public override Guid ComponentGuid => new Guid("BF517D60-B853-4C61-9574-AD8A718B995B");

  public FilterPropertiesByPropertyGroupPaths()
    : base(
      "FilterPropertiesByPropertyGroupPaths",
      "pgF",
      "Filters object properties by their property group path",
      "Speckle",
      "Properties"
    ) { }

  protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
  {
    pManager.AddParameter(
      new SpeckleObjectParam(),
      "Object",
      "O",
      "Speckle Object to filter properties from",
      GH_ParamAccess.item
    );
    pManager.AddTextParameter("Paths", "P", "Property Group paths to filter by", GH_ParamAccess.list);
  }

  protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
  {
    // pManager.AddParameter(      new SpecklePropertyParam(),      "Properties",      "P",      "The properties of the selected Object",      GH_ParamAccess.tree    );
  }

  protected override void SolveInstance(IGH_DataAccess da)
  {
    List<string> paths = new();
    da.GetDataList(1, paths);

    if (paths.Count == 0)
    {
      return;
    }

    SpeckleObjectWrapperGoo objectWrapperGoo = new();
    da.GetData(0, ref objectWrapperGoo);

    if (objectWrapperGoo.Value == null)
    {
      return;
    }

    SpecklePropertyGroupGoo properties = objectWrapperGoo.Value.Properties;
    if (properties.Value.Count == 0)
    {
      return;
    }

    var outputParams = new List<OutputParamWrapper>();
    foreach (var path in paths)
    {
      SpecklePropertyGoo result = FindProperty(properties, path);

      var param = new Param_GenericObject()
      {
        Name = result.Path,
        NickName = result.Path,
        Access = GH_ParamAccess.item
      };

      outputParams.Add(new OutputParamWrapper(param, result));

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
        for (int i = 0; i < outputParams.Count; i++)
        {
          var outParam = Params.Output[i];
          var outParamWrapper = outputParams[i];
          switch (outParam.Access)
          {
            case GH_ParamAccess.item:
              da.SetData(i, outParamWrapper.Values);
              break;
          }
        }
      }
    }
  }

  private SpecklePropertyGoo FindProperty(SpecklePropertyGroupGoo root, string unifiedPath)
  {
    if (!root.Value.TryGetValue(unifiedPath, out SpecklePropertyGoo currentGoo))
    {
      return new() { Path = unifiedPath, Value = "" };
    }

    return currentGoo;
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
    // TODO: better, nicer handling of creation/removal
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
        Access = newParam.Param.Access
      };
      Params.RegisterOutputParam(param);
    }

    Params.OnParametersChanged();
    VariableParameterMaintenance();
    ExpireSolution(false);
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

  public void VariableParameterMaintenance() { }
}

public record OutputParamWrapper(Param_GenericObject Param, object Values);
