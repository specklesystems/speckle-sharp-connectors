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
  /// </summary>
  /// <param name="tableName">String identifying the table to fetch</param>
  /// <param name="indexingColumn">Primary column to use as row identifier (default "UniqueName")</param>
  /// <param name="requestedColumns">Optional specific fields to fetch</param>
  /// <param name="additionalKeyColumns">Optional additional columns to form compound keys for tables with non-unique primary keys</param>
  /// <returns>TableData containing the requested fields and records</returns>
  public TableData GetTableData(
    string tableName,
    string? indexingColumn = null,
    string[]? requestedColumns = null,
    string[]? additionalKeyColumns = null
  )
  {
    // Create a cache key that includes additionalKeyColumns if provided
    string tableKeyField = indexingColumn ?? DEFAULT_KEY_FIELD; // most queries will use "UniqueName"
    string additionalKeysString = additionalKeyColumns != null ? string.Join(",", additionalKeyColumns) : "";
    string cacheKey = $"{tableName}_{tableKeyField}_{additionalKeysString}";

    if (_tableCache.TryGetValue(cacheKey, out var cachedData))
    {
      return cachedData;
    }

    var tableData = FetchTableData(tableName, tableKeyField, requestedColumns, additionalKeyColumns);
    _tableCache[cacheKey] = tableData;
    return tableData;
  }

  private TableData FetchTableData(
    string tableName,
    string indexingColumn,
    string[]? requestedColumns = null,
    string[]? additionalKeyColumns = null
  )
  {
    string[] requestedFields = requestedColumns ?? []; // only fetch the keys needed
    string[] fieldsKeysIncluded = [];
    string[] tableData = []; // one-dimensional array
    int tableVersion = 0;
    int numberOfRecords = 0;

    // Ensure indexingColumn is included
    if (requestedFields != Array.Empty<string>() && !requestedFields.Contains(indexingColumn))
    {
      requestedFields = [.. requestedFields, indexingColumn];
    }

    var result = _settingsStore.Current.SapModel.DatabaseTables.GetTableForDisplayArray(
      tableName,
      ref requestedFields,
      string.Empty, // empty means all objects
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

    return new TableData(fieldsKeysIncluded, tableData, numberOfRecords, indexingColumn, additionalKeyColumns);
  }
}
