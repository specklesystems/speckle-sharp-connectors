namespace Speckle.Converters.CSiShared.Extensions;

/// <summary>
/// Csi Api returns a one-dimensional array of the table data. Any cDatabaseTable queries will require some processing.
/// The TableData extension processes queries for GetTableForDisplayArray.
/// </summary>
/// <remarks>
/// TableData implemented as a record. Reasons for this include:
/// <list type="bullet">
///     <item><description>Keeping data immutable (preventing accidental modifications).</description></item>
///     <item><description>Better choice for large data sets (heap allocation).</description></item>
/// </list>
/// Notes:
/// <list type="bullet">
///     <item><description>A cDatabaseTable query returns ALL objects of a type. This is an expensive operation. However, the typical use-case involves sending the entire Etabs/Sap model.</description></item>
///     <item><description>High initial memory usage when creating dictionaries for all rows of data.</description></item>
///     <item><description>Benefits of the dictionary evident during send operations when most/all objects are sent (and thus queried).</description></item>
///     <item><description>Single upfront dictionary creation preferred over repeated on-demand creation</description></item>
///     <item><description>Yes, Csi returns all data as strings. Even int, double etc.</description></item>
/// </list>
/// </remarks>
public record TableData
{
  private readonly string[] _columnNames; // "fieldKeys" in api docs
  private readonly string[] _rawTableData; // raw, one-dimensional array of table data (before processing)
  private readonly int _rowCount; // number of rows
  private readonly string _indexColumn; // column used to index/identify rows (typically, "UniqueName")
  private readonly string[]? _additionalKeyColumns; // optional additional columns for compound keys (e.g. repeating "UniqueName")

  private IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? _processedRows; // cached data structure

  /// <summary>
  /// Creates a new TableData instance for processing CSI database table data.
  /// </summary>
  /// <param name="columnNames">Array of column names in the table</param>
  /// <param name="rawTableData">Raw 1D array of table data from CSI API</param>
  /// <param name="rowCount">Number of rows in the table</param>
  /// <param name="indexColumn">Primary column to use as row identifier</param>
  /// <param name="additionalKeyColumns">Optional additional columns to form compound keys for tables with non-unique primary keys</param>
  public TableData(
    string[] columnNames,
    string[] rawTableData,
    int rowCount,
    string indexColumn,
    string[]? additionalKeyColumns = null
  )
  {
    _columnNames = columnNames;
    _rawTableData = rawTableData;
    _rowCount = rowCount;
    _indexColumn = indexColumn;
    _additionalKeyColumns = additionalKeyColumns;
  }

  /// <summary>
  /// Gets table data as a dictionary mapping indexColumn (typically "UniqueName" to _processedRows).
  /// Each row is itself a dictionary mapping column names to their values. Computed once on first access and cached.
  /// </summary>
  /// <remarks>
  /// Motivation:
  /// <list type="bullet">
  ///     <item><description>One-dimensional array => structured dictionary format</description></item>
  ///     <item><description>Each row keyed by its "UniqueName" value</description></item>
  ///     <item><description>Each row value is itself a dictionary of field keys to values</description></item>
  /// </list>
  /// When additionalKeyColumns are provided, keys are formed by combining values from all key columns
  /// using a pipe separator (|).
  ///
  /// If additionalKeyColumns are not provided and the table has multiple rows with the same primary key,
  /// only the last row for each key will be preserved, and a warning will be logged.
  /// </remarks>
  public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Rows
  {
    get
    {
      if (_processedRows != null) // Lazy loading - only build dictionary when first accessed
      {
        return _processedRows;
      }

      var columnsPerRow = _columnNames.Length;
      var indexColumnIndex = Array.IndexOf(_columnNames, _indexColumn);

      if (indexColumnIndex == -1)
      {
        throw new InvalidOperationException(
          $"Row data structured according specified '{_indexColumn}' field. This was not found in the database."
        );
      }

      // Get indices for additional key columns if provided
      int[] additionalKeyIndices = [];
      if (_additionalKeyColumns != null && _additionalKeyColumns.Length > 0)
      {
        additionalKeyIndices = new int[_additionalKeyColumns.Length];
        for (int i = 0; i < _additionalKeyColumns.Length; i++)
        {
          additionalKeyIndices[i] = Array.IndexOf(_columnNames, _additionalKeyColumns[i]);
          if (additionalKeyIndices[i] == -1)
          {
            throw new InvalidOperationException(
              $"Additional key column '{_additionalKeyColumns[i]}' not found in the database."
            );
          }
        }
      }

      // Pre-size dictionary with known capacity
      var rows = new Dictionary<string, IReadOnlyDictionary<string, string>>(_rowCount);
      var keysSeen = new HashSet<string>(); // Track keys to detect duplicates

      // Create a field index lookup to avoid repeated Array.IndexOf calls
      var fieldIndexLookup = new Dictionary<string, int>(columnsPerRow);
      for (int i = 0; i < _columnNames.Length; i++)
      {
        fieldIndexLookup[_columnNames[i]] = i;
      }

      // Process each row
      bool hasMultipleRowsPerKey = false;
      for (int rowStart = 0; rowStart < _rawTableData.Length; rowStart += columnsPerRow)
      {
        // Get the primary key value
        var primaryKeyValue = _rawTableData[rowStart + indexColumnIndex];

        // Construct the full key (either just primary key or compound key)
        string fullKey;
        if (additionalKeyIndices.Length > 0)
        {
          // Build compound key with additional columns
          var keyParts = new string[1 + additionalKeyIndices.Length];
          keyParts[0] = primaryKeyValue;

          for (int i = 0; i < additionalKeyIndices.Length; i++)
          {
            keyParts[i + 1] = _rawTableData[rowStart + additionalKeyIndices[i]];
          }

          fullKey = string.Join("|", keyParts);
        }
        else
        {
          fullKey = primaryKeyValue;
        }

        // Check if this key has been seen before (only matters if no additionalKeyColumns)
        if (additionalKeyIndices.Length == 0 && keysSeen.Contains(primaryKeyValue))
        {
          hasMultipleRowsPerKey = true;
        }
        keysSeen.Add(primaryKeyValue);

        // Create row dictionary
        var row = new Dictionary<string, string>(columnsPerRow, StringComparer.Ordinal);
        foreach (var kvp in fieldIndexLookup)
        {
          row[kvp.Key] = _rawTableData[rowStart + kvp.Value];
        }

        rows[fullKey] = row;
      }

      if (hasMultipleRowsPerKey && additionalKeyIndices.Length == 0)
      {
        System.Diagnostics.Debug.WriteLine(
          $"WARNING: Table has multiple rows with the same primary key '{_indexColumn}'. "
            + "Only the last row for each key is preserved. Consider specifying additionalKeyColumns "
            + "when calling GetTableData to create compound keys."
        );
      }

      _processedRows = rows;
      return _processedRows;
    }
  }

  /// <summary>
  /// Retrieves a string value from a specific row and column from the table data.
  /// </summary>
  /// <param name="rowKey">The unique identifier for the row, matching the value in the index column (e.g., "UniqueName")</param>
  /// <param name="columnName">The name of the column containing the desired value</param>
  /// <returns>The string value found at the specified row and column intersection</returns>
  /// <exception cref="InvalidOperationException">Thrown when either the row or column is not found in the table</exception>
  public string GetRowValue(string rowKey, string columnName)
  {
    if (TryGetValue(rowKey, columnName, out var value))
    {
      return value;
    }

    throw new InvalidOperationException($"Failed to get value for row '{rowKey}', column '{columnName}'");
  }

  private bool TryGetValue(string rowKey, string columnName, out string value)
  {
    if (Rows.TryGetValue(rowKey, out var row) && row.TryGetValue(columnName, out value!))
    {
      return true;
    }

    value = string.Empty;
    return false;
  }

  /// <summary>
  /// Indicates whether this TableData was created with compound keys (additionalKeyColumns).
  /// </summary>
  public bool HasCompoundKeys => _additionalKeyColumns != null && _additionalKeyColumns.Length > 0;
}
