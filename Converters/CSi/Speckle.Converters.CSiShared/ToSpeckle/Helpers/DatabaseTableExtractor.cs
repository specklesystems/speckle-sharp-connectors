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
  private const string DEFAULT_KEY_FIELD = "UniqueName";

  public DatabaseTableExtractor(IConverterSettingsStore<CsiConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
    _tableCache = [];
  }

  /// <summary>
  /// Uses the cDatabaseTables.GetTableForDisplayArray() to request data for a specified table name.
  /// Processes the one-dimensional array return with the <see cref="Speckle.Converters.CSiShared.Extensions.TableData"/>
  /// extension for improved workability/reliability.
  /// </summary>
  /// <param name="tableName">String identifying the table to fetch. This typically matches the UI.</param>
  /// <param name="indexingColumn">Key used to organize and (later) lookup specific rows of data. Optional argument, default is "UniqueName"</param>
  /// <param name="requestedColumns">Optional list of specific fields to fetch. If null or empty, all fields will be returned. Ask Björn about how to determine these strings.</param>
  /// <returns>TableData containing the requested fields and records</returns>
  public TableData GetTableData(string tableName, string? indexingColumn = null, string[]? requestedColumns = null)
  {
    string tableKeyField = indexingColumn ?? DEFAULT_KEY_FIELD; // most queries will use "UniqueName"
    string cacheKey = $"{tableName}_{tableKeyField}";
    if (_tableCache.TryGetValue(cacheKey, out var cachedData))
    {
      return cachedData;
    }

    var tableData = FetchTableData(tableName, tableKeyField, requestedColumns);
    _tableCache[cacheKey] = tableData;
    return tableData;
  }

  public void RefreshTable(string tableKey, string? keyField = null) =>
    _tableCache.Remove($"{tableKey}_{keyField ?? DEFAULT_KEY_FIELD}");

  public void ClearCache() => _tableCache.Clear();

  private TableData FetchTableData(string tableName, string indexingColumn, string[]? requestedColumns = null)
  {
    string[] requestedFields = requestedColumns ?? []; // only fetch the keys needed (memory reduction potential)
    string[] fieldsKeysIncluded = [];
    string[] tableData = []; // one-dimensional gross mess
    int tableVersion = 0;
    int numberOfRecords = 0;

    // ensure indexingColumn is included in the requested fields
    // if user forgets to include indexingColumn in requestedColumns => problem when it comes to creating dictionaries!
    if (requestedFields != Array.Empty<string>() && !requestedFields.Contains(indexingColumn))
    {
      requestedFields = [.. requestedFields, indexingColumn];
    }

    var result = _settingsStore.Current.SapModel.DatabaseTables.GetTableForDisplayArray(
      tableName,
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
        $"Failed to fetch table data for {tableName}. Check correctness of tableName and requestedColumns."
      );
    }

    return new TableData(fieldsKeysIncluded, tableData, numberOfRecords, indexingColumn);
  }
}
