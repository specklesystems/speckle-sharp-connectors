using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Speckle.Connectors.GrasshopperShared.Components.BaseComponents;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;

namespace Speckle.Connectors.GrasshopperShared.Components.Objects;

/// <summary>
/// Given a list of objects, this component will filter the list for objects that match the queries.
/// </summary>
[Guid("26AEA046-4DD4-4F61-8251-E92A6D2AC880")]
public class FilterSpeckleObjects : GH_Component
{
  public override Guid ComponentGuid => GetType().GUID;
  protected override Bitmap Icon => Resources.speckle_objects_filter;
  public override GH_Exposure Exposure => GH_Exposure.primary;

  public FilterSpeckleObjects()
    : base(
      "Filter Objects",
      "fO",
      "Filters a list of Speckle Objects according to inputs. Filter methods: Equals (default), StartsWith(<), EndsWith(>) , Contains(?), Regex(;)",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.OBJECTS
    ) { }

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddGenericParameter("Objects", "O", "Speckle Objects to filter", GH_ParamAccess.list);

    pManager.AddTextParameter("Name", "N", "Find objects with a matching name", GH_ParamAccess.item);
    Params.Input[1].Optional = true;

    pManager.AddTextParameter(
      "Property Key",
      "P",
      "Find objects with a property that has a matching key",
      GH_ParamAccess.item
    );
    Params.Input[2].Optional = true;

    pManager.AddTextParameter(
      "Material Name",
      "M",
      "Find objects with a render material that has a matching name",
      GH_ParamAccess.item
    );
    Params.Input[3].Optional = true;

    pManager.AddTextParameter(
      "Application Id",
      "aID",
      "Find objects with a matching applicationId",
      GH_ParamAccess.item
    );
    Params.Input[4].Optional = true;

    pManager.AddTextParameter("Speckle Id", "sID", "Find objects with a matching Speckle id", GH_ParamAccess.item);
    Params.Input[5].Optional = true;
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    pManager.AddGenericParameter("Objects", "O", "The objects that match the queries", GH_ParamAccess.tree);

    pManager.AddGenericParameter(
      "Culled Objects",
      "co",
      "The objects that did not match the queries",
      GH_ParamAccess.tree
    );
  }

  protected override void SolveInstance(IGH_DataAccess dataAccess)
  {
    List<IGH_Goo> inputObjects = new();
    dataAccess.GetDataList(0, inputObjects);

    if (inputObjects.Count == 0)
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Add objects to filter");
      return;
    }

    List<SpeckleWrapper?> objects = inputObjects
      .Select(o => o.ToSpeckleObjectWrapper())
      .Where(o => o is not null)
      .ToList();

    int unsupported = inputObjects.Count - objects.Count;
    if (unsupported > 0)
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Input contained {unsupported} unsupported objects.");
      return;
    }

    string name = "";
    dataAccess.GetData(1, ref name);
    string property = "";
    dataAccess.GetData(2, ref property);
    string material = "";
    dataAccess.GetData(3, ref material);
    string appId = "";
    dataAccess.GetData(4, ref appId);
    string speckleId = "";
    dataAccess.GetData(5, ref speckleId);

    List<SpeckleWrapper> matchedObjects = new();
    List<SpeckleWrapper> removedObjects = new();
    for (int i = 0; i < objects.Count; i++)
    {
      SpeckleWrapper wrapper = objects[i]!;

      // filter by name
      if (!MatchesSearchPattern(name, wrapper.Name))
      {
        removedObjects.Add(wrapper);
        continue;
      }

      // filter by property
      bool foundProperty = false;
      if (string.IsNullOrEmpty(property))
      {
        foundProperty = true;
      }
      else
      {
        SpecklePropertyGroupGoo? properties = wrapper is SpeckleDataObjectWrapper dataObjPropWrapper
          ? dataObjPropWrapper.Properties
          : wrapper is SpeckleGeometryWrapper geoPropWrapper
            ? geoPropWrapper.Properties
            : null;

        if (properties is not null)
        {
          // use flattened properties to search ALL nested property keys
          // fix for [CNX-2512](https://linear.app/speckle/issue/CNX-2512/filter-objects-material-and-property-key-inputs-dont-work-as-expected)
          Dictionary<string, SpecklePropertyGoo> flattenedProps = properties.Flatten();
          foreach (string key in flattenedProps.Keys)
          {
            if (MatchesSearchPattern(property, key))
            {
              foundProperty = true;
              break;
            }
          }
        }
      }

      if (!foundProperty)
      {
        removedObjects.Add(wrapper);
        continue;
      }

      // filter by material name
      bool materialMatches = true;
      if (!string.IsNullOrEmpty(material))
      {
        materialMatches = false;

        if (wrapper is SpeckleGeometryWrapper geoWrapper)
        {
          materialMatches = MatchesSearchPattern(material, geoWrapper.Material?.Name ?? "");
        }
        else if (wrapper is SpeckleDataObjectWrapper dataObjWrapper)
        {
          // check if ANY geometry in the data object has a matching material (not sure about this...)
          // fix for [CNX-2512](https://linear.app/speckle/issue/CNX-2512/filter-objects-material-and-property-key-inputs-dont-work-as-expected)
          materialMatches = dataObjWrapper.Geometries.Any(geo =>
            MatchesSearchPattern(material, geo.Material?.Name ?? "")
          );
        }
      }

      if (!materialMatches)
      {
        removedObjects.Add(wrapper);
        continue;
      }

      // filter by application id
      if (!MatchesSearchPattern(appId, wrapper.Base.applicationId ?? ""))
      {
        removedObjects.Add(wrapper);
        continue;
      }

      // filter by speckle id
      if (!MatchesSearchPattern(speckleId, wrapper.Base.id ?? ""))
      {
        removedObjects.Add(wrapper);
        continue;
      }

      matchedObjects.Add(wrapper);
    }

    // Set output objects
    dataAccess.SetDataList(0, matchedObjects.Select(o => o.CreateGoo()));
    dataAccess.SetDataList(1, removedObjects.Select(o => o.CreateGoo()));
  }

  private bool MatchesSearchPattern(string searchPattern, string target)
  {
    if (string.IsNullOrEmpty(searchPattern))
    {
      return true;
    }

    return Operator.IsSymbolNameLike(target, searchPattern);
  }
}
