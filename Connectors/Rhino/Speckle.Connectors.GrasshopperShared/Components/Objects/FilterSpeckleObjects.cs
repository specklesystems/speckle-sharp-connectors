using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
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
public class FilterSpeckleObjects : GH_Component, IGH_VariableParameterComponent
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

    // optional parameters - only read if they've been added via ⊕
    string appId = "";
    string speckleId = "";
    int? appIdIndex = FindInputIndexByName("Application Id");
    int? speckleIdIndex = FindInputIndexByName("Speckle Id");

    if (appIdIndex.HasValue)
    {
      dataAccess.GetData(appIdIndex.Value, ref appId);
    }

    if (speckleIdIndex.HasValue)
    {
      dataAccess.GetData(speckleIdIndex.Value, ref speckleId);
    }

    bool filterByAppId = appIdIndex.HasValue;
    bool filterBySpeckleId = speckleIdIndex.HasValue;

    List<SpeckleWrapper> matchedObjects = new();
    List<SpeckleWrapper> removedObjects = new();

    foreach (SpeckleWrapper wrapper in objects.Cast<SpeckleWrapper>())
    {
      if (MatchesAllFilters(wrapper, name, property, material, appId, filterByAppId, speckleId, filterBySpeckleId))
      {
        matchedObjects.Add(wrapper);
      }
      else
      {
        removedObjects.Add(wrapper);
      }
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

  /// <summary>
  /// Determines if a wrapper matches all active filter criteria.
  /// </summary>
  private bool MatchesAllFilters(
    SpeckleWrapper wrapper,
    string name,
    string property,
    string material,
    string appId,
    bool filterByAppId,
    string speckleId,
    bool filterBySpeckleId
  )
  {
    // filter by name
    if (!MatchesSearchPattern(name, wrapper.Name))
    {
      return false;
    }

    // filter by property
    if (!MatchesPropertyFilter(wrapper, property))
    {
      return false;
    }

    // filter by material name
    if (!MatchesMaterialFilter(wrapper, material))
    {
      return false;
    }

    // filter by application id (only if parameter was added)
    if (filterByAppId && !MatchesSearchPattern(appId, wrapper.Base.applicationId ?? ""))
    {
      return false;
    }

    // filter by speckle id (only if parameter was added)
    if (filterBySpeckleId && !MatchesSearchPattern(speckleId, wrapper.Base.id ?? ""))
    {
      return false;
    }

    return true;
  }

  private bool MatchesPropertyFilter(SpeckleWrapper wrapper, string property)
  {
    if (string.IsNullOrEmpty(property))
    {
      return true;
    }

    SpecklePropertyGroupGoo? properties = wrapper is SpeckleDataObjectWrapper dataObjPropWrapper
      ? dataObjPropWrapper.Properties
      : wrapper is SpeckleGeometryWrapper geoPropWrapper
        ? geoPropWrapper.Properties
        : null;

    if (properties is null)
    {
      return false;
    }

    // use flattened properties to search ALL nested property keys
    return properties.Flatten().Keys.Any(key => MatchesSearchPattern(property, key));
  }

  private bool MatchesMaterialFilter(SpeckleWrapper wrapper, string material)
  {
    if (string.IsNullOrEmpty(material))
    {
      return true;
    }

    if (wrapper is SpeckleGeometryWrapper geoWrapper)
    {
      return MatchesSearchPattern(material, geoWrapper.Material?.Name ?? "");
    }

    if (wrapper is SpeckleDataObjectWrapper dataObjWrapper)
    {
      // check if ANY geometry in the data object has a matching material
      return dataObjWrapper.Geometries.Any(geo => MatchesSearchPattern(material, geo.Material?.Name ?? ""));
    }

    return false;
  }

  /// <summary>
  /// Finds the index of an input parameter by its Name.
  /// Returns null if the parameter doesn't exist.
  /// </summary>
  private int? FindInputIndexByName(string paramName)
  {
    for (int i = 0; i < Params.Input.Count; i++)
    {
      if (Params.Input[i].Name == paramName)
      {
        return i;
      }
    }
    return null;
  }

  #region IGH_VariableParameterComponent

  public bool CanInsertParameter(GH_ParameterSide side, int index)
  {
    if (side != GH_ParameterSide.Input)
    {
      return false;
    }

    // only allow inserting after the fixed parameters (index 4+)
    if (index < 4)
    {
      return false;
    }

    // check how many optional params are already added (total inputs - 4 fixed)
    int addedOptionalCount = Params.Input.Count - 4;

    // we have 2 optional parameters available
    return addedOptionalCount < 2;
  }

  public bool CanRemoveParameter(GH_ParameterSide side, int index) =>
    // only allow removing optional input parameters (index 4+)
    side == GH_ParameterSide.Input
    && index >= 4;

  /// <remarks>
  /// The ternary operator for NickName is needed due to a Grasshopper quirk where
  /// dynamically created parameters don't respect the "Draw Full Names" setting automatically.
  /// We check CanvasFullNames at creation time to set the appropriate NickName.
  /// This does not handle the case where the user toggles "Draw Full Names" while the
  /// component is already on the canvas. Handling that would require subscribing to
  /// Grasshopper.CentralSettings.CanvasFullNamesChanged event, which is overkill for now.
  /// </remarks>
  public IGH_Param CreateParameter(GH_ParameterSide side, int index)
  {
    bool hasAppId = FindInputIndexByName("Application Id").HasValue;
    bool hasSpeckleId = FindInputIndexByName("Speckle Id").HasValue;

    if (!hasAppId)
    {
      return new Param_String
      {
        Name = "Application Id",
        NickName = Grasshopper.CentralSettings.CanvasFullNames ? "Application Id" : "aID", // see remarks
        Description = "Find objects with a matching applicationId",
        Access = GH_ParamAccess.item,
        Optional = true,
      };
    }

    if (!hasSpeckleId)
    {
      return new Param_String
      {
        Name = "Speckle Id",
        NickName = Grasshopper.CentralSettings.CanvasFullNames ? "Speckle Id" : "sID", // see remarks
        Description = "Find objects with a matching Speckle id",
        Access = GH_ParamAccess.item,
        Optional = true,
      };
    }

    return new Param_String();
  }

  public bool DestroyParameter(GH_ParameterSide side, int index) => side == GH_ParameterSide.Input && index >= 4;

  public void VariableParameterMaintenance()
  {
    // ensure all optional parameters stay marked as optional
    for (int i = 4; i < Params.Input.Count; i++)
    {
      Params.Input[i].Optional = true;
    }
  }

  #endregion
}
