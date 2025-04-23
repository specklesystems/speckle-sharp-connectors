using Grasshopper.Kernel.Types;
using Speckle.Connectors.Common.Extensions;
using Speckle.Connectors.GrasshopperShared.Components.BaseComponents;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;
#if RHINO8_OR_GREATER
using Grasshopper.Rhinoceros.Model;
#endif

namespace Speckle.Connectors.GrasshopperShared.Components.Properties;

public class PropertyGroupPathsSelector : ValueSet<IGH_Goo>
{
  public PropertyGroupPathsSelector()
    : base(
      "Property Selector",
      "pSelect",
      "Allows you to select a set of property keys for querying",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.OBJECTS
    ) { }

  public override Guid ComponentGuid => new Guid("8882BE3A-81F1-4416-B420-58D69E4CC8F1");

  protected override Bitmap Icon => Resources.speckle_inputs_property;

  protected override void LoadVolatileData()
  {
    var objectPropertyGroups = VolatileData
      .AllData(true)
      .OfType<SpeckleObjectWrapperGoo>()
      .Select(goo => goo.Value.Properties.Value)
      .ToList();

#if RHINO8_OR_GREATER
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
#endif

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
