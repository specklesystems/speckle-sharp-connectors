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
  private readonly string[] _fieldKeys; // Column names
  private readonly string[] _data; // One-dimensional array of table data
  private readonly int _numberRecords; // Number of rows
  private IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? _rows; // Cached data structure

  public TableData(string[] fieldKeys, string[] data, int numberRecords)
  {
    _fieldKeys = fieldKeys;
    _data = data;
    _numberRecords = numberRecords;
  }

  public IReadOnlyList<string> FieldKeys => _fieldKeys;

  /// <summary>
  /// Gets table data as a dictionary mapping UniqueName to row data.
  /// Each row is itself a dictionary mapping field keys to their values.
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
      if (_rows != null) // Lazy loading - only build dictionary when first accessed
      {
        return _rows;
      }

      var fieldsPerRow = _fieldKeys.Length;
      var uniqueNameIndex = Array.IndexOf(_fieldKeys, "UniqueName");

      if (uniqueNameIndex == -1)
      {
        throw new InvalidOperationException(
          "Row data structured according to 'UniqueName' field. This keys must be present in the database."
        ); // TODO: Reassess when we get to analysis results
      }

      // Pre-size dictionary with known capacity
      var rows = new Dictionary<string, IReadOnlyDictionary<string, string>>(_numberRecords);

      // Create a field index lookup to avoid repeated Array.IndexOf calls
      var fieldIndexLookup = new Dictionary<string, int>(fieldsPerRow);
      for (int i = 0; i < _fieldKeys.Length; i++)
      {
        fieldIndexLookup[_fieldKeys[i]] = i;
      }

      // Process each row
      for (int rowStart = 0; rowStart < _data.Length; rowStart += fieldsPerRow)
      {
        var uniqueName = _data[rowStart + uniqueNameIndex];

        // Pre-size the row dictionary
        var row = new Dictionary<string, string>(fieldsPerRow, StringComparer.Ordinal);

        // Use index lookup instead of repeated string comparisons
        foreach (var kvp in fieldIndexLookup)
        {
          row[kvp.Key] = _data[rowStart + kvp.Value];
        }

        rows[uniqueName] = row;
      }

      _rows = rows;
      return _rows;
    }
  }
}
