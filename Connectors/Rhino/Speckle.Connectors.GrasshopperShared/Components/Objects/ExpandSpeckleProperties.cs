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

  protected override void RegisterInputParams(GH_InputParamManager pManager) =>
    pManager.AddParameter(
      new SpecklePropertyGroupParam(),
      "Properties",
      "P",
      "Speckle Properties to expand",
      GH_ParamAccess.item
    );

  protected override void RegisterOutputParams(GH_OutputParamManager pManager) { }

  protected override void SolveInstance(IGH_DataAccess da)
  {
    // ALWAYS run port generation on the first iteration, BEFORE validating the current item
    // ensure that a null at index 0 doesn't prevent ports from being created.
    if (da.Iteration == 0)
    {
      // gather all property groups from the input (skipNulls = true)
      var allData = Params.Input[0].VolatileData.AllData(true).OfType<SpecklePropertyGroupGoo>().ToList();

      // guard against empty data on file load / async operations to prevent stale ports from dropping (CNX-3245)
      if (allData.Count > 0)
      {
        var outputParamsDict = new Dictionary<string, OutputParamWrapper>();

        foreach (var propGroup in allData)
        {
          if (propGroup?.Value == null)
          {
            continue;
          }

          foreach (var key in propGroup.Value.Keys)
          {
            ISpecklePropertyGoo value = propGroup.Value[key];
            object? outputValue = value switch
            {
              SpecklePropertyGoo prop => prop.Value,
              SpecklePropertyGroupGoo pg => pg,
              _ => value,
            };

            if (!outputParamsDict.TryGetValue(key, out var existingWrapper))
            {
              var param = new SpeckleOutputParam
              {
                Name = key,
                NickName = key,
                Access = outputValue is IList ? GH_ParamAccess.list : GH_ParamAccess.item,
              };
              outputParamsDict[key] = new OutputParamWrapper(param, outputValue);
            }
            else if (existingWrapper.Param.Access == GH_ParamAccess.item && outputValue is IList)
            {
              existingWrapper.Param.Access = GH_ParamAccess.list;
            }
          }
        }

        var outputParams = outputParamsDict.Values.ToList();

        Name = $"Properties ({outputParams.Count})";
        NickName = Name;

        if (OutputMismatch(outputParams))
        {
          OnPingDocument()?.ScheduleSolution(5, _ => CreateOutputs(outputParams));
          return;
        }
      }
    }

    SpecklePropertyGroupGoo? properties = null;
    if (!da.GetData(0, ref properties) || properties?.Value == null)
    {
      return;
    }

    for (int i = 0; i < Params.Output.Count; i++)
    {
      var outParam = Params.Output[i];

      if (properties.Value.TryGetValue(outParam.Name, out ISpecklePropertyGoo? value))
      {
        object? outputValue = value switch
        {
          SpecklePropertyGoo prop => prop.Value,
          SpecklePropertyGroupGoo propGroup => propGroup,
          _ => value,
        };

        if (outParam.Access == GH_ParamAccess.item)
        {
          da.SetData(i, outputValue);
        }
        else
        {
          da.SetDataList(i, outputValue as IList ?? new List<object?>());
        }
      }
    }
  }

  /// <summary>
  /// Creates output parameters based on discovered properties.
  /// </summary>
  private void CreateOutputs(List<OutputParamWrapper> outputParams)
  {
    bool needsMaintenance = false;

    // remove old parameters that are no longer present
    for (int i = Params.Output.Count - 1; i >= 0; i--)
    {
      var existingParam = Params.Output[i];
      if (outputParams.All(p => p.Param.Name != existingParam.Name))
      {
        Params.UnregisterOutputParameter(existingParam);
        needsMaintenance = true;
      }
    }

    // add new parameters and update existing ones in place to preserve wires
    for (int i = 0; i < outputParams.Count; i++)
    {
      var targetParam = outputParams[i].Param;
      var existingParam = Params.Output.FirstOrDefault(p => p.Name == targetParam.Name);

      if (existingParam != null)
      {
        if (existingParam.Access != targetParam.Access)
        {
          existingParam.Access = targetParam.Access;
          needsMaintenance = true;
        }

        if (existingParam.NickName != targetParam.NickName)
        {
          existingParam.NickName = targetParam.NickName;
          needsMaintenance = true;
        }

        int currentIndex = Params.Output.IndexOf(existingParam);
        if (currentIndex != i)
        {
          Params.Output.RemoveAt(currentIndex);
          Params.Output.Insert(i, existingParam);
          needsMaintenance = true;
        }
      }
      else
      {
        var newParam = new SpeckleOutputParam
        {
          Name = targetParam.Name,
          NickName = targetParam.NickName,
          MutableNickName = false,
          Access = targetParam.Access,
        };
        Params.RegisterOutputParam(newParam, i);
        needsMaintenance = true;
      }
    }

    if (needsMaintenance)
    {
      // notify gh of parameter changes
      Params.OnParametersChanged();
      VariableParameterMaintenance();
      ExpireSolution(false);
    }
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
