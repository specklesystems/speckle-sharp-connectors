using System.Collections;
using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;

namespace Speckle.Connectors.GrasshopperShared.Components.Objects;

// NOTE: Why all this madness? The properties passthrough node is restrictive in output type being uniform
// Properties whose values were lists were not being displayed and couldn't be given back to the user as native
// lists. This was (it seemed) the only viable approach.
// [CNX-2364](https://linear.app/speckle/issue/CNX-2364/grasshopper-properties-passthrough-does-not-handle-list-values)

[Guid("474F4699-D641-444F-BC78-E22AAF40B240")]
public class ExpandSpeckleProperties : GH_Component, IGH_VariableParameterComponent
{
  public ExpandSpeckleProperties()
    : base(
      "Expand Properties",
      "eP",
      "Expands Speckle Properties into their individual outputs with correct access types",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.OBJECTS
    ) { }

  public override Guid ComponentGuid => GetType().GUID;
  protected override Bitmap Icon => Resources.speckle_properties_expand;
  public override GH_Exposure Exposure => GH_Exposure.secondary;

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddParameter(
      new SpecklePropertyGroupParam(),
      "Properties",
      "P",
      "Speckle Properties to expand",
      GH_ParamAccess.item
    );
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager) { }

  protected override void SolveInstance(IGH_DataAccess da)
  {
    SpecklePropertyGroupGoo? properties = null;
    if (!da.GetData(0, ref properties) || properties?.Value == null)
    {
      return;
    }

    Name = $"Properties ({properties.Value.Count})";
    NickName = Name;

    var outputParams = new List<OutputParamWrapper>();

    foreach (var key in properties.Value.Keys)
    {
      ISpecklePropertyGoo value = properties.Value[key];
      object? outputValue = value switch
      {
        SpecklePropertyGoo prop => prop.Value,
        SpecklePropertyGroupGoo propGroup => propGroup,
        _ => value
      };

      var param = new SpeckleOutputParam
      {
        Name = key,
        NickName = key,
        Access = outputValue is IList ? GH_ParamAccess.list : GH_ParamAccess.item
      };

      outputParams.Add(new OutputParamWrapper(param, outputValue));
    }

    // handle parameter creation/update (only on first iteration)
    if (da.Iteration == 0 && OutputMismatch(outputParams))
    {
      OnPingDocument()?.ScheduleSolution(5, _ => CreateOutputs(outputParams));
    }
    else
    {
      for (int i = 0; i < outputParams.Count; i++)
      {
        var outputParam = outputParams[i];
        switch (outputParam.Param.Access)
        {
          case GH_ParamAccess.item:
            da.SetData(i, outputParam.Value);
            break;
          case GH_ParamAccess.list:
            da.SetDataList(i, outputParam.Value as IList ?? new List<object?>());
            break;
        }
      }
    }
  }

  /// <summary>
  /// Creates output parameters based on discovered properties.
  /// </summary>
  private void CreateOutputs(List<OutputParamWrapper> outputParams)
  {
    // remove all existing output parameters
    while (Params.Output.Count > 0)
    {
      Params.UnregisterOutputParameter(Params.Output[^1]);
    }

    // add new output parameters
    foreach (var newParam in outputParams)
    {
      var param = new SpeckleOutputParam
      {
        Name = newParam.Param.Name,
        NickName = newParam.Param.NickName,
        MutableNickName = false,
        Access = newParam.Param.Access
      };
      Params.RegisterOutputParam(param);
    }

    // notify gh of parameter changes
    Params.OnParametersChanged();
    VariableParameterMaintenance();
    ExpireSolution(false);
  }

  /// <summary>
  /// Determines if the current output parameter structure differs from the required structure.
  /// </summary>
  private bool OutputMismatch(List<OutputParamWrapper> outputParams)
  {
    if (Params.Output.Count != outputParams.Count)
    {
      return true;
    }

    for (int i = 0; i < outputParams.Count; i++)
    {
      var newParam = outputParams[i];
      var oldParam = Params.Output[i];
      if (
        oldParam.NickName != newParam.Param.NickName
        || oldParam.Name != newParam.Param.Name
        || oldParam.Access != newParam.Param.Access
      )
      {
        return true;
      }
    }

    return false;
  }

  // IGH_VariableParameterComponent implementation
  public bool CanInsertParameter(GH_ParameterSide side, int index) => false;

  public bool CanRemoveParameter(GH_ParameterSide side, int index) => false;

  public void VariableParameterMaintenance() { }

  public IGH_Param CreateParameter(GH_ParameterSide side, int index) => new SpeckleOutputParam();

  public bool DestroyParameter(GH_ParameterSide side, int index) => side == GH_ParameterSide.Output;
}

public record OutputParamWrapper(SpeckleOutputParam Param, object? Value);
