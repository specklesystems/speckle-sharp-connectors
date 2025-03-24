using Grasshopper.Kernel;
using Speckle.Connectors.Grasshopper8.Parameters;
using Speckle.Sdk;

namespace Speckle.Connectors.Grasshopper8.Components.Properties;

public class FilterPropertiesByPropertyGroupPaths : GH_Component
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
    pManager.AddTextParameter("Path", "P", "Property Group path to filter by", GH_ParamAccess.item);
  }

  protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
  {
    pManager.AddParameter(
      new SpecklePropertyParam(),
      "Properties",
      "P",
      "The properties of the selected Object",
      GH_ParamAccess.tree
    );
  }

  protected override void SolveInstance(IGH_DataAccess dataAccess)
  {
    string path = "";
    dataAccess.GetData(1, ref path);

    if (string.IsNullOrEmpty(path))
    {
      return;
    }

    SpeckleObjectGoo objectGoo = new();
    dataAccess.GetData(0, ref objectGoo);

    if (objectGoo.Value == null)
    {
      return;
    }

    if (objectGoo.Value.Base["properties"] is not Dictionary<string, object?> propertyDict)
    {
      return;
    }

    KeyValuePair<string, object?> targetProperty = FindProperty(propertyDict, path);
    SpecklePropertyGoo result = new(targetProperty);
    dataAccess.SetData(0, result);
  }

  private KeyValuePair<string, object?> FindProperty(Dictionary<string, object?> root, string unifiedPath)
  {
    List<string> propertyKeyNames = unifiedPath.Split(["."], StringSplitOptions.None).ToList();
    Dictionary<string, object?> currentPropertyGroup = root;
    while (propertyKeyNames.Count != 0)
    {
      string currentKey = propertyKeyNames.First();

      if (!currentPropertyGroup.TryGetValue(currentKey, out object? currentValue))
      {
        throw new SpeckleException($"Did not find property group key: {propertyKeyNames.First()}");
      }

      propertyKeyNames.RemoveAt(0);
      if (currentValue is Dictionary<string, object?> childGroup)
      {
        currentPropertyGroup = childGroup;
      }
      else
      {
        if (propertyKeyNames.Count == 0)
        {
          return new(currentKey, currentValue);
        }
        else
        {
          throw new SpeckleException($"Property group key: {currentKey} did not yield another property group.");
        }
      }
    }

    throw new SpeckleException("Did not find property");
  }
}
