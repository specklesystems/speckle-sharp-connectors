using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Speckle.Connectors.GrasshopperShared.HostApp.Extras;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;

namespace Speckle.Connectors.GrasshopperShared.Components.Objects;

[Guid("A3FD5CBF-DFB0-44DF-9988-04466EB8E5E6")]
public class CreateSpeckleProperties : GH_Component, IGH_VariableParameterComponent
{
  public CreateSpeckleProperties()
    : base(
      "Create Properties",
      "CP",
      "Creates a set of properties for Speckle objects",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.OBJECTS
    ) { }

  public override Guid ComponentGuid => GetType().GUID;

  protected override Bitmap Icon => Resources.speckle_properties_create;

  public bool CreateEmptyProperties { get; set; }

  private readonly DebounceDispatcher _debounceDispatcher = new();

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    var p = CreateParameter(GH_ParameterSide.Input, 0);
    pManager.AddParameter(p);
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    pManager.AddGenericParameter("Properties", "P", "Properties for Speckle Objects", GH_ParamAccess.item);
  }

  protected override void SolveInstance(IGH_DataAccess da)
  {
    // Create a data tree to store output
    Dictionary<string, SpecklePropertyGoo> properties = new();

    // Check for structure of all inputs to see matching branches
    foreach (var inputParam in Params.Input)
    {
      string inputName = inputParam.NickName;

      if (properties.ContainsKey(inputName))
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Duplicate property name found: {inputName}.");
        return;
      }

      properties.Add(inputName, new());
    }

    for (int i = 0; i < Params.Input.Count; i++)
    {
      object? value = null;
      da.GetData(i, ref value);

      // POC: for now, allow empty properties
      if (value == null)
      {
        properties[Params.Input[i].NickName] = new SpecklePropertyGoo();
      }
      else
      {
        SpecklePropertyGoo actualValue = new();
        if (!actualValue.CastFrom(value))
        {
          AddRuntimeMessage(
            GH_RuntimeMessageLevel.Error,
            $"Parameter {Params.Input[i].NickName} should not contain anything other than strings, doubles, ints, and bools."
          );

          return;
        }

        properties[Params.Input[i].NickName] = actualValue;
      }
    }

    var groupGoo = new SpecklePropertyGroupGoo(properties);
    da.SetData(0, groupGoo);
  }

  public bool CanInsertParameter(GH_ParameterSide side, int index)
  {
    return side == GH_ParameterSide.Input && !CreateEmptyProperties;
  }

  public bool CanRemoveParameter(GH_ParameterSide side, int index)
  {
    return side == GH_ParameterSide.Input;
  }

  public IGH_Param CreateParameter(GH_ParameterSide side, int index)
  {
    var myParam = new Param_GenericObject
    {
      Name = $"Property {Params.Input.Count + 1}",
      MutableNickName = true,
      Optional = true,
      Access = GH_ParamAccess.item
    };

    myParam.NickName = myParam.Name;
    myParam.Optional = true;
    return myParam;
  }

  public bool DestroyParameter(GH_ParameterSide side, int index)
  {
    return side == GH_ParameterSide.Input;
  }

  public void VariableParameterMaintenance()
  {
    // todo
  }

  public override void AddedToDocument(GH_Document document)
  {
    base.AddedToDocument(document);
    Params.ParameterChanged += (sender, args) =>
    {
      if (args.ParameterSide == GH_ParameterSide.Output)
      {
        return;
      }

      switch (args.OriginalArguments.Type)
      {
        case GH_ObjectEventType.NickName:
          // This means the user is typing characters, debounce until it stops for 400ms before expiring the solution.
          // Prevents UI from locking too soon while writing new names for inputs.
          args.Parameter.Name = args.Parameter.NickName;
          _debounceDispatcher.Debounce(500, e => ExpireSolution(true));
          break;
        case GH_ObjectEventType.NickNameAccepted:
          args.Parameter.Name = args.Parameter.NickName;
          ExpireSolution(true);
          break;
        case GH_ObjectEventType.Sources:
          // if this event is a source change, and param is the last input, then add a new param automatically
          if (args.Parameter.SourceCount > 0 && args.ParameterIndex == Params.Input.Count - 1)
          {
            IGH_Param param = CreateParameter(GH_ParameterSide.Input, Params.Input.Count);
            Params.RegisterInputParam(param);
            Params.OnParametersChanged();
          }
          break;
      }
    };
  }

  public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
  {
    base.AppendAdditionalMenuItems(menu);

    Menu_AppendSeparator(menu);
    ToolStripMenuItem emptyPropsMenuItem = Menu_AppendItem(
      menu,
      "Create empty Properties",
      (s, e) =>
      {
        CreateEmptyProperties = !CreateEmptyProperties;
        if (CreateEmptyProperties)
        {
          Params.Input.Clear();
          ClearData();
        }
        else if (Params.Input.Count == 0)
        {
          var p = CreateParameter(GH_ParameterSide.Input, 0);
          Params.RegisterInputParam(p);
        }
        ExpireSolution(true);
      },
      true,
      CreateEmptyProperties
    );
    emptyPropsMenuItem.ToolTipText =
      "Toggle creating empty Properties. If set, the output Properties will be empty. Use for removing properties from objects.";
  }
}
