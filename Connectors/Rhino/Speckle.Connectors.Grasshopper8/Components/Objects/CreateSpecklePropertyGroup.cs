using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Speckle.Connectors.Grasshopper8.HostApp;
using Speckle.Connectors.Grasshopper8.HostApp.Extras;
using Speckle.Connectors.Grasshopper8.Parameters;

namespace Speckle.Connectors.Grasshopper8.Components.Objects;

[Guid("A3FD5CBF-DFB0-44DF-9988-04466EB8E5E6")]
public class CreateSpecklePropertyGroup : GH_Component, IGH_VariableParameterComponent
{
  public CreateSpecklePropertyGroup()
    : base(
      "Create Speckle Property Group",
      "CSO",
      "Creates a property group for Speckle objects",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.OBJECTS
    ) { }

  public override Guid ComponentGuid => GetType().GUID;

  protected override Bitmap Icon => BitmapBuilder.CreateCircleIconBitmap("cP");

  private readonly DebounceDispatcher _debounceDispatcher = new();

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    var p = CreateParameter(GH_ParameterSide.Input, 0);
    pManager.AddParameter(p);
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    pManager.AddGenericParameter("Property Group", "P", "Group of properties that was created", GH_ParamAccess.tree);
  }

  protected override void SolveInstance(IGH_DataAccess da)
  {
    Dictionary<string, SpecklePropertyGoo> properties = new();
    foreach (var inputParam in Params.Input)
    {
      if (properties.ContainsKey(inputParam.NickName))
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Duplicate property name found: {inputParam.NickName}.");

        return;
      }

      var data = inputParam.VolatileData.AllData(true).OfType<object>().ToList();

      if (data.Count > 1)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Cannot support list properties yet.");
        return;
      }

      SpecklePropertyGoo propGoo = new();
      if (!propGoo.CastFrom(data.Count == 0 ? "" : data.First()))
      {
        AddRuntimeMessage(
          GH_RuntimeMessageLevel.Error,
          $"Parameter {inputParam.NickName} should not contain anything other than strings, doubles, ints, and bools."
        );

        return;
      }

      propGoo.Path = inputParam.Name;
      properties.Add(inputParam.Name, propGoo);
    }

    da.SetData(0, new SpecklePropertyGroupGoo(properties));
  }

  public bool CanInsertParameter(GH_ParameterSide side, int index)
  {
    return side == GH_ParameterSide.Input;
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
    // TODO?
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
      }
    };
  }
}
