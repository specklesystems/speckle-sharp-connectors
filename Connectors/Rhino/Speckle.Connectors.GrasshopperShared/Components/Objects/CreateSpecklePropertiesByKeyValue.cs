using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;

namespace Speckle.Connectors.GrasshopperShared.Components.Objects;

/// <summary>
/// CreateSpeckleProperties passthrough component by key value pairs
/// </summary>
[Guid("FED2298C-0D2B-4868-94B5-B8D17F9385A5")]
public class CreateSpecklePropertiesByKeyValue : GH_Component
{
  public override Guid ComponentGuid => GetType().GUID;
  protected override Bitmap Icon => Resources.speckle_properties_create;

  public CreateSpecklePropertiesByKeyValue()
    : base(
      "Create Properties KVP",
      "CP",
      "Creates a set of properties for Speckle objects by keyvalue",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.OBJECTS
    ) { }

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddParameter(new SpecklePropertyGroupParam(), "Properties", "P", "Input properties", GH_ParamAccess.item);
    Params.Input[0].Optional = true;

    pManager.AddTextParameter("Keys", "k", "Keys of all properties", GH_ParamAccess.list);
    Params.Input[1].Optional = true;

    pManager.AddGenericParameter(
      "Values",
      "v",
      "Values of all properties. Accepts text, number, bool, null, or other properties as inputs.",
      GH_ParamAccess.list
    );
    Params.Input[2].Optional = true;
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    pManager.AddParameter(new SpecklePropertyGroupParam(), "Properties", "P", "Properties", GH_ParamAccess.item);
    pManager.AddTextParameter("Keys", "k", "Keys of all properties", GH_ParamAccess.list);
    pManager.AddGenericParameter("Values", "v", "Values of all properties", GH_ParamAccess.list);
  }

  protected override void SolveInstance(IGH_DataAccess da)
  {
    SpecklePropertyGroupGoo? inputProperties = null;
    da.GetData(0, ref inputProperties);

    List<string> inputKeys = new();
    bool hasKeys = da.GetDataList(1, inputKeys);

    List<object?> inputValues = new();
    bool hasValues = da.GetDataList(2, inputValues);

    // if no props or keyvalues were input, create empty props and return
    if (inputProperties == null && !hasKeys)
    {
      SpecklePropertyGroupGoo emptyProps = new();
      da.SetData(0, emptyProps);
      da.SetDataList(1, emptyProps.Value.Keys);
      da.SetDataList(1, emptyProps.Value.Values);
      return;
    }

    // validate that keys and values are of same length
    if (hasKeys && hasValues && inputKeys.Count != inputValues.Count)
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Keys and values are mismatched in length");
      return;
    }

    // process the properties
    Dictionary<string, ISpecklePropertyGoo> result = inputProperties is null ? new() : inputProperties.Value;

    // process keys and values
    if (hasKeys)
    {
      // check for duplicate keys
      var duplicates = inputKeys.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key);
      if (duplicates.Any())
      {
        AddRuntimeMessage(
          GH_RuntimeMessageLevel.Error,
          $"Duplicate property keys found: {string.Join(", ", duplicates)}"
        );
        return;
      }

      // set keyvalue pairs
      result.Clear();
      for (int i = 0; i < inputKeys.Count; i++)
      {
        object? value = inputValues[i];
        ISpecklePropertyGoo? convertedValue = null;
        switch (value)
        {
          case SpecklePropertyGroupGoo propGoo:
            convertedValue = propGoo;
            break;
          case null:
            convertedValue = new SpecklePropertyGoo();
            break;
          default:
            SpecklePropertyGoo propValue = new();
            if (!propValue.CastFrom(value))
            {
              AddRuntimeMessage(
                GH_RuntimeMessageLevel.Error,
                $"Values contain an invalid data type. Only strings, numbers, booleans, and other Speckle properties are supported."
              );
              return;
            }

            convertedValue = propValue;
            break;
        }

        result.Add(inputKeys[i], convertedValue);
      }
    }

    var groupGoo = new SpecklePropertyGroupGoo(result);
    da.SetData(0, groupGoo);
    da.SetDataList(1, result.Keys);
    da.SetDataList(1, result.Values);
  }
}
