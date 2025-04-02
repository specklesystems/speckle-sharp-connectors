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

  // TODO: Column forces only for columns in the selection
  // TODO: Check that we actually get results back. Maybe user hasn't run analysis model => no results to extract
  // TODO: Etabs "refresh" which entails deselection all load cases and combos, then "selecting" appropriate ones
  public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> GetColumnsForces() =>
    _databaseTableExtractor.GetTableData(TABLE_KEY).Rows;
}
