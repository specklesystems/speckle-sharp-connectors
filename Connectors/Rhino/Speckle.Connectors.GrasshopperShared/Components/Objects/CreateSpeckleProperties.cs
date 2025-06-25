using System.Runtime.InteropServices;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Speckle.Connectors.GrasshopperShared.Components.BaseComponents;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;

namespace Speckle.Connectors.GrasshopperShared.Components.Objects;

/// <summary>
/// Simplified CreateSpeckleProperties component using the base class pattern
/// </summary>
[Guid("A3FD5CBF-DFB0-44DF-9988-04466EB8E5E6")]
public class CreateSpeckleProperties : VariableParameterComponentBase
{
  private bool CreateEmptyProperties { get; set; }

  public override Guid ComponentGuid => GetType().GUID;
  protected override Bitmap Icon => Resources.speckle_properties_create;
  public override GH_Exposure Exposure => GH_Exposure.tertiary;

  public CreateSpeckleProperties()
    : base(
      "Create Properties",
      "CP",
      "Creates a set of properties for Speckle objects",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.OBJECTS
    ) { }

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    var param = CreateParameter(GH_ParameterSide.Input, 0);
    pManager.AddParameter(param);
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    pManager.AddGenericParameter("Properties", "P", "Properties for Speckle Objects", GH_ParamAccess.item);
  }

  protected override void SolveInstance(IGH_DataAccess da)
  {
    var properties = new Dictionary<string, ISpecklePropertyGoo>();

    // Validate for duplicate names
    var paramNames = Params.Input.Select(p => p.NickName).ToList();
    var duplicates = paramNames.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key);

    if (duplicates.Any())
    {
      AddRuntimeMessage(
        GH_RuntimeMessageLevel.Error,
        $"Duplicate property names found: {string.Join(", ", duplicates)}"
      );
      return;
    }

    // Process each input parameter
    for (int i = 0; i < Params.Input.Count; i++)
    {
      var paramName = Params.Input[i].NickName;
      var propertyValue = ExtractPropertyValue(da, i, paramName);

      if (propertyValue != null)
      {
        properties[paramName] = propertyValue;
      }
    }

    var groupGoo = new SpecklePropertyGroupGoo(properties);
    da.SetData(0, groupGoo);
  }

  private ISpecklePropertyGoo? ExtractPropertyValue(IGH_DataAccess da, int index, string paramName)
  {
    object? value = null;
    da.GetData(index, ref value);

    // check for a group input first
    if (value is SpecklePropertyGroupGoo group)
    {
      return group;
    }

    var propertyGoo = new SpecklePropertyGoo();
    if (value == null)
    {
      return propertyGoo; // Return empty property
    }

    if (!propertyGoo.CastFrom(value))
    {
      AddRuntimeMessage(
        GH_RuntimeMessageLevel.Error,
        $"Parameter '{paramName}' contains invalid data type. Only strings, numbers, booleans, and other Speckle properties are supported."
      );
      return null;
    }

    return propertyGoo;
  }

  protected override void AppendComponentSpecificMenuItems(ToolStripDropDown menu)
  {
    Menu_AppendSeparator(menu);
    var emptyPropsMenuItem = Menu_AppendItem(
      menu,
      "Create empty Properties",
      (_, _) => ToggleCreateEmptyProperties(),
      true,
      CreateEmptyProperties
    );
    emptyPropsMenuItem.ToolTipText = "Creates empty properties. Use for removing properties from objects.";
  }

  private void ToggleCreateEmptyProperties()
  {
    CreateEmptyProperties = !CreateEmptyProperties;

    if (CreateEmptyProperties)
    {
      Params.Input.Clear();
      ClearData();
    }
    else if (Params.Input.Count == 0)
    {
      var param = CreateParameter(GH_ParameterSide.Input, 0);
      Params.RegisterInputParam(param);
    }

    ExpireSolution(true);
  }

  protected override void WriteComponentSpecificData(GH_IWriter writer)
  {
    writer.SetBoolean("CreateEmptyProperties", CreateEmptyProperties);
  }

  protected override void ReadComponentSpecificData(GH_IReader reader)
  {
    bool createEmpty = default;
    if (reader.TryGetBoolean("CreateEmptyProperties", ref createEmpty))
    {
      CreateEmptyProperties = createEmpty;
    }
  }

  // IGH_VariableParameterComponent implementation
  public override bool CanInsertParameter(GH_ParameterSide side, int index) =>
    side == GH_ParameterSide.Input && !CreateEmptyProperties;

  public override bool CanRemoveParameter(GH_ParameterSide side, int index) => side == GH_ParameterSide.Input;

  public override bool DestroyParameter(GH_ParameterSide side, int index) => side == GH_ParameterSide.Input;

  public override IGH_Param CreateParameter(GH_ParameterSide side, int index)
  {
    var param = CreateVariableParameter($"Property {Params.Input.Count + 1}", GH_ParamAccess.item);
    return param;
  }
}
