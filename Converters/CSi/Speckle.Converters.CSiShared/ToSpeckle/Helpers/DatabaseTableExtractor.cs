using Speckle.Converters.Common;
using Speckle.Converters.CSiShared.Extensions;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

/// <summary>
/// Implementation of database table extraction and caching for CSI API.
/// </summary>
/// <remarks>
/// In the current context, an interface was not deemed necessary. We only have this implementation.
/// Consider introducing an interface IDatabaseTableExtractor if the need arises.
/// </remarks>
public class DatabaseTableExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;
  private readonly Dictionary<string, TableData> _tableCache;

  public DatabaseTableExtractor(IConverterSettingsStore<CsiConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
    _tableCache = [];
  }

  public TableData GetTableData(string tableKey, string[]? fieldKeyList = null)
  {
    if (_tableCache.TryGetValue(tableKey, out var cachedData))
    {
      return cachedData;
    }

    var tableData = FetchTableData(tableKey, fieldKeyList);
    _tableCache[tableKey] = tableData;
    return tableData;
  }

  public void RefreshTable(string tableKey) => _tableCache.Remove(tableKey);

  public void ClearCache() => _tableCache.Clear();

  /// <summary>
  /// Uses the cDatabaseTables.GetTableForDisplayArray() to request data for a specified table key.
  /// Processes the one-dimensional array return with the <see cref="Speckle.Converters.CSiShared.Extensions.TableData"/>
  /// extension for improved workability/reliability.
  /// </summary>
  /// <param name="tableKey">The key identifying the table to fetch. This typically matches the UI string.</param>
  /// <param name="fieldKeyList">Optional list of specific fields to fetch. If null or empty, all fields will be returned. Ask Björn about how to determine these strings.</param>
  /// <returns>TableData containing the requested fields and records</returns>
  /// <exception cref="InvalidOperationException">Thrown when the database query fails</exception>
  private TableData FetchTableData(string tableKey, string[]? fieldKeyList = null)
  {
    string[] requestedFields = fieldKeyList ?? []; // only fetch the keys needed (memory reduction potential). Since we depend on "UniqueName", this should probably be in no matter what. i.e. check fieldKeyList for "UniqueName", if not there, add it.
    string[] fieldsKeysIncluded = [];
    string[] tableData = [];
    int tableVersion = 0;
    int numberOfRecords = 0;

    var result = _settingsStore.Current.SapModel.DatabaseTables.GetTableForDisplayArray(
      tableKey,
      ref requestedFields,
      string.Empty, // empty means all objects (not group-specific)
      ref tableVersion,
      ref fieldsKeysIncluded,
      ref numberOfRecords,
      ref tableData
    );

    if (result != 0)
    {
      throw new InvalidOperationException(
        $"Failed to fetch table data for {tableKey}. Check correctness of tableKey and fieldKeyList."
      );
    }

    return new TableData(fieldsKeysIncluded, tableData, numberOfRecords);
  }
}
