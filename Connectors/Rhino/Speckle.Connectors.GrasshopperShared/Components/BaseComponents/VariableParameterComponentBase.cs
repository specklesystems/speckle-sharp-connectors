using GH_IO.Serialization;
using Grasshopper.Kernel;
using Speckle.Connectors.GrasshopperShared.HostApp.Extras;
using Speckle.Connectors.GrasshopperShared.Parameters;

namespace Speckle.Connectors.GrasshopperShared.Components.BaseComponents;

/// <summary>
/// Base class for variable parameter components with name inheritance support
/// </summary>
public abstract class VariableParameterComponentBase : GH_Component, IGH_VariableParameterComponent
{
  private readonly DebounceDispatcher _debounceDispatcher = new();
  private bool _alwaysInheritNames;

  protected VariableParameterComponentBase(
    string name,
    string nickname,
    string description,
    string category,
    string subCategory
  )
    : base(name, nickname, description, category, subCategory) { }

  public bool AlwaysInheritNames
  {
    get => _alwaysInheritNames;
    set
    {
      if (_alwaysInheritNames != value)
      {
        _alwaysInheritNames = value;
        OnAlwaysInheritNamesChanged();
        UpdateDisplayMessage();
      }
    }
  }

  protected virtual void UpdateDisplayMessage()
  {
    Message = AlwaysInheritNames ? "Inheriting names" : "";
  }

  private void OnAlwaysInheritNamesChanged()
  {
    // Update all existing SpeckleVariableParams
    foreach (var param in Params.Input.OfType<SpeckleVariableParam>())
    {
      param.AlwaysInheritNames = AlwaysInheritNames;
    }
    OnDisplayExpired(true);
  }

  protected SpeckleVariableParam CreateVariableParameter(string baseName, GH_ParamAccess access)
  {
    var param = new SpeckleVariableParam
    {
      Name = baseName,
      NickName = baseName,
      MutableNickName = true,
      Optional = true,
      Access = access,
      CanInheritNames = true,
      AlwaysInheritNames = AlwaysInheritNames,
    };

    // Subscribe to the parameter's name changes for auto-resizing
    param.ObjectChanged += OnParameterObjectChanged;

    return param;
  }

  /// <summary>
  /// Handles parameter object changes, including name changes that require resizing
  /// </summary>
  private void OnParameterObjectChanged(IGH_DocumentObject sender, GH_ObjectChangedEventArgs e)
  {
    // Only respond to name changes that affect layout
    if (e.Type == GH_ObjectEventType.NickName || e.Type == GH_ObjectEventType.NickNameAccepted)
    {
      // Force immediate component resize for name inheritance
      TriggerComponentResize();
    }
  }

  private void TriggerComponentResize()
  {
    // Simple: just expire the layout - Grasshopper handles the rest
    Attributes?.ExpireLayout();
  }

  private void OnParameterNameChanged(IGH_Param parameter)
  {
    parameter.Name = parameter.NickName;
    _debounceDispatcher.Debounce(500, _ => ExpireSolution(true));
  }

  private void OnParameterSourceChanged(IGH_Param parameter, int parameterIndex)
  {
    // Auto-add parameter if connecting to the last input
    if (parameter.SourceCount > 0 && parameterIndex == Params.Input.Count - 1)
    {
      var newParam = CreateParameter(GH_ParameterSide.Input, Params.Input.Count);
      Params.RegisterInputParam(newParam);
      Params.OnParametersChanged();
    }
  }

  public override void AddedToDocument(GH_Document document)
  {
    base.AddedToDocument(document);
    Params.ParameterChanged += OnParameterChanged;

    // Ensure all existing parameters are properly subscribed
    foreach (var param in Params.Input.OfType<SpeckleVariableParam>())
    {
      param.ObjectChanged -= OnParameterObjectChanged; // Remove any existing subscription
      param.ObjectChanged += OnParameterObjectChanged; // Add fresh subscription
    }
  }

  public override void RemovedFromDocument(GH_Document document)
  {
    Params.ParameterChanged -= OnParameterChanged;

    // Clean up parameter event subscriptions
    foreach (var param in Params.Input.OfType<SpeckleVariableParam>())
    {
      param.ObjectChanged -= OnParameterObjectChanged;
    }

    base.RemovedFromDocument(document);
  }

  private void OnParameterChanged(object sender, GH_ParamServerEventArgs args)
  {
    if (args.ParameterSide != GH_ParameterSide.Input)
    {
      return;
    }

    switch (args.OriginalArguments.Type)
    {
      case GH_ObjectEventType.NickName:
        OnParameterNameChanged(args.Parameter);
        break;
      case GH_ObjectEventType.NickNameAccepted:
        args.Parameter.Name = args.Parameter.NickName;
        ExpireSolution(true);
        break;
      case GH_ObjectEventType.Sources:
        OnParameterSourceChanged(args.Parameter, args.ParameterIndex);
        break;
    }
  }

  public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
  {
    base.AppendAdditionalMenuItems(menu);

    Menu_AppendSeparator(menu);
    var alwaysInheritMenuItem = Menu_AppendItem(
      menu,
      "Always inherit names",
      (_, _) => AlwaysInheritNames = !AlwaysInheritNames,
      true,
      AlwaysInheritNames
    );
    alwaysInheritMenuItem.ToolTipText = "Parameters auto-inherit source names and update automatically";

    AppendComponentSpecificMenuItems(menu);
  }

  protected virtual void AppendComponentSpecificMenuItems(ToolStripDropDown menu)
  {
    // Override in derived classes for component-specific menu items
  }

  public override bool Write(GH_IWriter writer)
  {
    var result = base.Write(writer);
    writer.SetBoolean("AlwaysInheritNames", AlwaysInheritNames);
    WriteComponentSpecificData(writer);
    return result;
  }

  public override bool Read(GH_IReader reader)
  {
    var result = base.Read(reader);
    bool alwaysInherit = default;
    if (reader.TryGetBoolean("AlwaysInheritNames", ref alwaysInherit))
    {
      AlwaysInheritNames = alwaysInherit;
      UpdateExistingParameters();
    }

    ReadComponentSpecificData(reader);
    return result;
  }

  private void UpdateExistingParameters()
  {
    foreach (var param in Params.Input.OfType<SpeckleVariableParam>())
    {
      param.AlwaysInheritNames = AlwaysInheritNames;
      param.ObjectChanged -= OnParameterObjectChanged;
      param.ObjectChanged += OnParameterObjectChanged;
    }
    UpdateDisplayMessage();
  }

  protected virtual void WriteComponentSpecificData(GH_IWriter writer) { }

  protected virtual void ReadComponentSpecificData(GH_IReader reader) { }

  // abstract methods to satisfy IGH_VariableParameterComponent
  public abstract bool CanInsertParameter(GH_ParameterSide side, int index);
  public abstract bool CanRemoveParameter(GH_ParameterSide side, int index);
  public abstract IGH_Param CreateParameter(GH_ParameterSide side, int index);
  public abstract bool DestroyParameter(GH_ParameterSide side, int index);

  public virtual void VariableParameterMaintenance() { }
}
