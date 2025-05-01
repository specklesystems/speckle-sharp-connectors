using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Speckle.Connectors.GrasshopperShared.Components.BaseComponents;
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

  public FilterSpeckleObjects()
    : base(
      "FilterSpeckleObjects",
      "fO",
      "Filters a list of Speckle Objects according to inputs. Filter methods: Equals (default), StartsWith(<), EndsWith(>) , Contains(?), Regex(;)",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.OBJECTS
    ) { }

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddParameter(new SpeckleObjectParam(), "Objects", "O", "Speckle Objects to filter", GH_ParamAccess.list);

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
    pManager.AddParameter(
      new SpeckleObjectParam(),
      "Objects",
      "O",
      "The objects that match the queries",
      GH_ParamAccess.tree
    );

    pManager.AddParameter(
      new SpeckleObjectParam(),
      "Culled Objects",
      "co",
      "The objects that did not match the queries",
      GH_ParamAccess.tree
    );
  }

  protected override void SolveInstance(IGH_DataAccess dataAccess)
  {
    List<SpeckleObjectWrapperGoo> inputObjects = new();
    dataAccess.GetDataList(0, inputObjects);

    if (inputObjects.Count == 0)
    {
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

    List<SpeckleObjectWrapper> matchedObjects = new();
    List<SpeckleObjectWrapper> removedObjects = new();
    for (int i = 0; i < inputObjects.Count; i++)
    {
      // filter by name
      if (!MatchesSearchPattern(name, inputObjects[i].Value.Name))
      {
        removedObjects.Add(inputObjects[i].Value);
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
        foreach (string key in inputObjects[i].Value.Properties.Value.Keys)
        {
          if (MatchesSearchPattern(property, key))
          {
            foundProperty = true;
            break;
          }
        }
      }

      if (!foundProperty)
      {
        removedObjects.Add(inputObjects[i].Value);
        continue;
      }

      // filter by material name
      if (!MatchesSearchPattern(material, inputObjects[i].Value.Material?.Name ?? ""))
      {
        removedObjects.Add(inputObjects[i].Value);
        continue;
      }

      // filter by application id
      if (!MatchesSearchPattern(appId, inputObjects[i].Value.Base.applicationId ?? ""))
      {
        removedObjects.Add(inputObjects[i].Value);
        continue;
      }

      // filter by speckle id
      if (!MatchesSearchPattern(speckleId, inputObjects[i].Value.Base.id ?? ""))
      {
        removedObjects.Add(inputObjects[i].Value);
        continue;
      }

      matchedObjects.Add(inputObjects[i].Value);
    }

    // Set output objects
    dataAccess.SetDataList(0, matchedObjects);
    dataAccess.SetDataList(1, removedObjects);
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
