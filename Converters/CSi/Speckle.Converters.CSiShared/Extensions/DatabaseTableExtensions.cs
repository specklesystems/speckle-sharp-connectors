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
  private readonly string[] _rawTableData; // indicating raw, one-dimensional array of table data (before processing)
  private readonly int _rowCount; // Number of rows
  private IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? _processedRows; // Cached data structure
  private readonly string _indexColumn; // column used to index/identify rows (typically, "UniqueName")

  public TableData(string[] columnNames, string[] rawTableData, int rowCount, string indexColumn)
  {
    _columnNames = columnNames;
    _rawTableData = rawTableData;
    _rowCount = rowCount;
    _indexColumn = indexColumn;
  }

  /// <summary>
  /// Gets table data as a dictionary mapping indexColumn (typically "UniqueName" to _processedRows).
  /// Each row is itself a dictionary mapping column names to their values.
  /// Computed once on first access and cached.
  /// </summary>
  /// <remarks>
  /// Motivation:
  /// <list type="bullet">
  ///     <item><description>One-dimensional array => structured dictionary format</description></item>
  ///     <item><description>Each row keyed by its "UniqueName" value</description></item>
  ///     <item><description>Each row value is itself a dictionary of field keys to values</description></item>
  /// </list>
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

      // Pre-size dictionary with known capacity
      var rows = new Dictionary<string, IReadOnlyDictionary<string, string>>(_rowCount);

      // Create a field index lookup to avoid repeated Array.IndexOf calls
      var fieldIndexLookup = new Dictionary<string, int>(columnsPerRow);
      for (int i = 0; i < _columnNames.Length; i++)
      {
        fieldIndexLookup[_columnNames[i]] = i;
      }

      // Process each row
      for (int rowStart = 0; rowStart < _rawTableData.Length; rowStart += columnsPerRow)
      {
        var keyValue = _rawTableData[rowStart + indexColumnIndex];

        // Pre-size the row dictionary
        var row = new Dictionary<string, string>(columnsPerRow, StringComparer.Ordinal);

        // Use index lookup instead of repeated string comparisons
        foreach (var kvp in fieldIndexLookup)
        {
          row[kvp.Key] = _rawTableData[rowStart + kvp.Value];
        }

        rows[keyValue] = row;
      }

      _processedRows = rows;
      return _processedRows;
    }
  }
}
