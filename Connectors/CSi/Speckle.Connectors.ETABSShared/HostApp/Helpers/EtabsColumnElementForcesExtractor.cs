using Speckle.Converters.CSiShared.ToSpeckle.Helpers;

namespace Speckle.Connectors.ETABSShared.HostApp.Helpers;

public class EtabsColumnElementForcesExtractor
{
  private readonly DatabaseTableExtractor _databaseTableExtractor;

  private const string TABLE_KEY = "Element Forces - Columns";

  public EtabsColumnElementForcesExtractor(DatabaseTableExtractor databaseTableExtractor)
  {
    _databaseTableExtractor = databaseTableExtractor;
  }

  /// <summary>
  /// Gets column forces organized hierarchically by element, load case, and station.
  /// </summary>
  /// <returns>
  /// A hierarchical dictionary structure with the following levels:
  /// - UniqueName: The structural element
  ///   - OutputCase: The load case or combination
  ///     - CaseType: Type of the load case (e.g., "Combination")
  ///     - Stations: Dictionary of stations along the element
  ///       - Station value: Dictionary of force components (P, V2, M3, etc.)
  /// </returns>
#pragma warning disable CA1024
  public Dictionary<string, Dictionary<string, object>> GetColumnsForces()
#pragma warning restore CA1024
  {
    // Request all relevant columns
    string[] requestedColumns = ["UniqueName", "OutputCase", "CaseType", "Station", "P", "V2", "V3", "T", "M2", "M3"];

    // Get table data
    var tableData = _databaseTableExtractor.GetTableData(
      TABLE_KEY,
      requestedColumns: requestedColumns,
      additionalKeyColumns: ["OutputCase", "Station"]
    );

    // Process into hierarchical structure
    return tableData.AsHierarchicalForces();
  }

  // TODO: Implement column forces filtering for selected elements only
  // TODO: Add validation to check if analysis model has been run
  // TODO: Implement load case/combo selection functionality
}
