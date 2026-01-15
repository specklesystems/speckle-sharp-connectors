using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;

namespace Speckle.Connectors.GrasshopperShared.Components;

/// <summary>
/// Base class for passthrough components with "hidden" Application ID parameter.
/// </summary>
/// <remarks>
/// Users can click ⊕ to add an optional Application ID input and output.
/// </remarks>
public abstract class SpecklePassthroughComponentBase : SpeckleSolveInstance, IGH_VariableParameterComponent
{
  private const string APP_ID_NAME = "Application Id";
  private const string APP_ID_NICKNAME = "aID";
  private const string APP_ID_DESCRIPTION = "The application id of the Speckle objects";

  protected abstract int FixedInputCount { get; }
  protected abstract int FixedOutputCount { get; }

  private bool HasApplicationIdParam => Params.Input.Count > FixedInputCount;

  protected SpecklePassthroughComponentBase(
    string name,
    string nickname,
    string description,
    string category,
    string subCategory
  )
    : base(name, nickname, description, category, subCategory) { }

  /// <summary>
  /// Reads the optional Application Id input. Returns true if user provided a valid value.
  /// </summary>
  protected bool TryGetApplicationIdInput(IGH_DataAccess da, out string? applicationId)
  {
    applicationId = null;

    if (!HasApplicationIdParam)
    {
      return false;
    }

    string appId = string.Empty;
    if (da.GetData(FixedInputCount, ref appId))
    {
      if (string.IsNullOrWhiteSpace(appId))
      {
        AddRuntimeMessage(
          GH_RuntimeMessageLevel.Warning,
          "Empty Application Id ignored - existing or auto-generated id will be used"
        );
        return false;
      }

      applicationId = appId;
      return true;
    }

    return false;
  }

  /// <summary>
  /// Sets the Application Id output (if the parameter exists).
  /// </summary>
  protected void SetApplicationIdOutput(IGH_DataAccess da, string? applicationId)
  {
    if (!HasApplicationIdParam)
    {
      return;
    }

    da.SetData(FixedOutputCount, applicationId);
  }

  public bool CanInsertParameter(GH_ParameterSide side, int index)
  {
    // only allow inserting if not yet added
    if (HasApplicationIdParam)
    {
      return false;
    }

    // only allow at the end position
    return side switch
    {
      GH_ParameterSide.Input => index == FixedInputCount,
      GH_ParameterSide.Output => index == FixedOutputCount,
      _ => false
    };
  }

  public bool CanRemoveParameter(GH_ParameterSide side, int index)
  {
    if (!HasApplicationIdParam)
    {
      return false;
    }

    return side switch
    {
      GH_ParameterSide.Input => index == FixedInputCount,
      GH_ParameterSide.Output => index == FixedOutputCount,
      _ => false
    };
  }

  /// <remarks>
  /// The ternary for NickName handles a Grasshopper quirk where dynamically created parameters
  /// don't respect the "Draw Full Names" setting automatically.
  /// </remarks>
  public IGH_Param CreateParameter(GH_ParameterSide side, int index)
  {
    // when adding on either side, add both input and output together
    if (side == GH_ParameterSide.Input && Params.Output.Count == FixedOutputCount)
    {
      OnPingDocument()?.ScheduleSolution(5, _ => AddApplicationIdOutput());
    }
    else if (side == GH_ParameterSide.Output && Params.Input.Count == FixedInputCount)
    {
      OnPingDocument()?.ScheduleSolution(5, _ => AddApplicationIdInput());
    }

    return CreateApplicationIdParam();
  }

  public bool DestroyParameter(GH_ParameterSide side, int index)
  {
    // when removing from either side, remove both input and output together
    if (side == GH_ParameterSide.Input && index == FixedInputCount && Params.Output.Count > FixedOutputCount)
    {
      OnPingDocument()?.ScheduleSolution(5, _ => RemoveApplicationIdOutput());
    }
    else if (side == GH_ParameterSide.Output && index == FixedOutputCount && Params.Input.Count > FixedInputCount)
    {
      OnPingDocument()?.ScheduleSolution(5, _ => RemoveApplicationIdInput());
    }

    return side switch
    {
      GH_ParameterSide.Input => index == FixedInputCount,
      GH_ParameterSide.Output => index == FixedOutputCount,
      _ => false
    };
  }

  public void VariableParameterMaintenance()
  {
    // ensure the Application Id input stays optional
    if (HasApplicationIdParam && Params.Input.Count > FixedInputCount)
    {
      Params.Input[FixedInputCount].Optional = true;
    }
  }

  private static IGH_Param CreateApplicationIdParam() =>
    new Param_String
    {
      Name = APP_ID_NAME,
      NickName = Grasshopper.CentralSettings.CanvasFullNames ? APP_ID_NAME : APP_ID_NICKNAME,
      Description = APP_ID_DESCRIPTION,
      Access = GH_ParamAccess.item,
      Optional = true
    };

  private void AddApplicationIdInput()
  {
    if (Params.Input.Count > FixedInputCount)
    {
      return;
    }

    Params.RegisterInputParam(CreateApplicationIdParam());
    Params.OnParametersChanged();
    VariableParameterMaintenance();
    ExpireSolution(true);
  }

  private void AddApplicationIdOutput()
  {
    if (Params.Output.Count > FixedOutputCount)
    {
      return;
    }

    Params.RegisterOutputParam(CreateApplicationIdParam());
    Params.OnParametersChanged();
    ExpireSolution(true);
  }

  private void RemoveApplicationIdInput()
  {
    if (Params.Input.Count <= FixedInputCount)
    {
      return;
    }

    Params.UnregisterInputParameter(Params.Input[FixedInputCount]);
    Params.OnParametersChanged();
    ExpireSolution(true);
  }

  private void RemoveApplicationIdOutput()
  {
    if (Params.Output.Count <= FixedOutputCount)
    {
      return;
    }

    Params.UnregisterOutputParameter(Params.Output[FixedOutputCount]);
    Params.OnParametersChanged();
    ExpireSolution(true);
  }

  public override bool Write(GH_IWriter writer)
  {
    var result = base.Write(writer);
    writer.SetBoolean("HasApplicationIdParam", HasApplicationIdParam);
    return result;
  }

  public override bool Read(GH_IReader reader)
  {
    var result = base.Read(reader);
    // parameters are restored by GH serialization, this flag is for reference
    bool hasAppIdParam = false;
    reader.TryGetBoolean("HasApplicationIdParam", ref hasAppIdParam);
    return result;
  }
}
