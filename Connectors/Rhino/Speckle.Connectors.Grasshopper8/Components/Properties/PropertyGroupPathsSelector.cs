using Grasshopper.Kernel.Types;
using Grasshopper.Rhinoceros.Model;
using Speckle.Connectors.Common.Extensions;
using Speckle.Connectors.Grasshopper8.Components.BaseComponents;
using Speckle.Connectors.Grasshopper8.Parameters;

namespace Speckle.Connectors.Grasshopper8.Components.Properties;

public class PropertyGroupPathsSelector : ValueSet<IGH_Goo>
{
  public PropertyGroupPathsSelector()
    : base(
      "Property Group Paths Selector",
      "Paths",
      "Allows you to select a set of property group paths for filtering",
      "Speckle",
      "Properties"
    ) { }

  public override Guid ComponentGuid => new Guid("8882BE3A-81F1-4416-B420-58D69E4CC8F1");

  protected override void LoadVolatileData()
  {
    var objectPropertyGroups = VolatileData
      .AllData(true)
      .OfType<SpeckleObjectWrapperGoo>()
      .Select(goo => goo.Value.Properties.Value)
      .ToList();

    // support model objects direct piping also
    if (objectPropertyGroups.Count != VolatileData.DataCount)
    {
      var modelObjects = VolatileData
        .AllData(true)
        .OfType<ModelObject>()
        .Select(mo => new SpeckleObjectWrapperGoo(mo).Value.Properties.Value)
        .ToList();
      objectPropertyGroups.AddRange(modelObjects);
    }

    if (objectPropertyGroups.Count == 0)
    {
      return;
    }

    var paths = GetPropertyPaths(objectPropertyGroups);
    m_data.AppendRange(paths.Select(s => new GH_String(s)));
  }

  private static List<string> GetPropertyPaths(List<Dictionary<string, SpecklePropertyGoo>> objectPropertyGroups)
  {
    var result = new HashSet<string>();
    foreach (var dict in objectPropertyGroups)
    {
      result.AddRange(
        dict.Keys.Where(k => !(k.EndsWith(".name") || k.EndsWith(".units") || k.EndsWith(".internalDefinitionName")))
      );
    }
    return result.ToList();
  }
}
