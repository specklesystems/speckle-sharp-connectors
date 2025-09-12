using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Speckle.Connectors.Common.Extensions;
using Speckle.Connectors.GrasshopperShared.Components.BaseComponents;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;

namespace Speckle.Connectors.GrasshopperShared.Components.Objects;

public class PropertyGroupPathsSelector : ValueSet<IGH_Goo>
{
  public PropertyGroupPathsSelector()
    : base(
      "Property Selector",
      "pSelect",
      "Allows you to select a set of property keys for querying. Right-click for 'Always select all' option.",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.OBJECTS
    ) { }

  public override Guid ComponentGuid => new Guid("8882BE3A-81F1-4416-B420-58D69E4CC8F1");
  protected override Bitmap Icon => Resources.speckle_inputs_property;
  public override GH_Exposure Exposure => GH_Exposure.quarternary;

  protected override void LoadVolatileData()
  {
    var propertyGroups = VolatileData
      .AllData(true)
      .Where(goo => goo is SpecklePropertyGroupGoo)
      .Select(goo =>
        goo switch
        {
          SpecklePropertyGroupGoo geometryGoo => geometryGoo,
          _ => throw new InvalidOperationException("Unexpected goo type")
        }
      )
      .ToList();

    if (propertyGroups.Count == 0)
    {
      return;
    }

    var paths = GetPropertyPaths(propertyGroups);
    m_data.Clear();
    m_data.AppendRange(paths.Select(s => new GH_String(s)));
  }

  private static List<string> GetPropertyPaths(List<SpecklePropertyGroupGoo> objectPropertyGroups)
  {
    var result = new HashSet<string>();
    foreach (SpecklePropertyGroupGoo propGroup in objectPropertyGroups)
    {
      // flatten the props
      Dictionary<string, SpecklePropertyGoo> flattenedProps = propGroup.Flatten();

      result.AddRange(
        flattenedProps.Keys.Where(k =>
          !(k.EndsWith(".name") || k.EndsWith(".units") || k.EndsWith(".internalDefinitionName"))
        )
      );
    }
    return result.ToList();
  }
}
