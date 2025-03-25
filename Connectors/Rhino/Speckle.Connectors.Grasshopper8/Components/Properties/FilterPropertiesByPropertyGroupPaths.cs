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

    SpeckleObjectWrapperGoo objectWrapperGoo = new();
    dataAccess.GetData(0, ref objectWrapperGoo);

    if (objectWrapperGoo.Value == null)
    {
      return;
    }

    Dictionary<string, SpecklePropertyGoo> properties = objectGoo.Value.Properties;
    if (properties.Count == 0)
    {
      return;
    }

    SpecklePropertyGoo result = FindProperty(properties, path);
    dataAccess.SetData(0, result);
  }

  private SpecklePropertyGoo FindProperty(Dictionary<string, SpecklePropertyGoo> root, string unifiedPath)
  {
    if (!root.TryGetValue(unifiedPath, out SpecklePropertyGoo currentGoo))
    {
      throw new SpeckleException($"Did not find property from path: {unifiedPath}");
    }

    return currentGoo;
  }
}
