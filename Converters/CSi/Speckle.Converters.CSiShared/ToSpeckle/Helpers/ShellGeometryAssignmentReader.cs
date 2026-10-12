using System.Globalization;
using Speckle.Converters.Common;
using Speckle.DoubleNumerics;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

internal sealed record ShellGeometryAssignments(Vector3[] Displacements, double[] Thicknesses);

internal sealed class ShellGeometryAssignmentReader(IConverterSettingsStore<CsiConversionSettings> settingsStore)
{
  private const string INSERTION_TABLE = "Area Assignments - Insertion Point";
  private const string THICKNESS_TABLE = "Area Assignments - Thickness Overwrites";
  private Dictionary<string, List<Dictionary<string, string>>>? _insertions;
  private Dictionary<string, List<Dictionary<string, string>>>? _thicknesses;

  public ShellGeometryAssignments Read(
    string name,
    IReadOnlyList<double> normalOffsets,
    IReadOnlyList<double> matrix,
    double sectionThickness
  )
  {
    var normal = Vector3.Normalize(new Vector3(matrix[2], matrix[5], matrix[8]));
    var local1 = new Vector3(matrix[0], matrix[3], matrix[6]);
    var local2 = new Vector3(matrix[1], matrix[4], matrix[7]);
    var displacements = normalOffsets.Select(offset => offset * normal).ToArray();
    var thicknesses = Enumerable.Repeat(sectionThickness, normalOffsets.Count).ToArray();
    LoadTables();
    if (_insertions!.TryGetValue(name, out var insertions))
    {
      string? coordinateSystem = null;
      foreach (var row in insertions)
      {
        if (row.TryGetValue("CoordSys", out string? rowSystem) && !string.IsNullOrEmpty(rowSystem))
        {
          coordinateSystem = rowSystem;
        }
        if (!row.TryGetValue("PointNumber", out string? pointNumber) || string.IsNullOrEmpty(pointNumber))
        {
          continue;
        }
        int corner = Corner(row, displacements.Length);
        var offset = new Vector3(Number(row, "Offset1"), Number(row, "Offset2"), Number(row, "Offset3"));
        var worldOffset = coordinateSystem switch
        {
          "Local" => offset.X * local1 + offset.Y * local2 + offset.Z * normal,
          "Global" => offset,
          _ => throw new InvalidOperationException("Unsupported shell offset coordinate system."),
        };
        // ENG-10472: GetOffsets3 already includes cardinal placement and the normal component of each joint offset.
        displacements[corner] += worldOffset - Vector3.Dot(worldOffset, normal) * normal;
      }
    }
    if (_thicknesses!.TryGetValue(name, out var overwrites))
    {
      foreach (var row in overwrites)
      {
        int corner = Corner(row, thicknesses.Length);
        double thickness = Number(row, "Thickness");
        if (thickness < 0)
        {
          throw new InvalidOperationException("Invalid shell thickness overwrite.");
        }
        thicknesses[corner] = thickness == 0 ? sectionThickness : thickness;
      }
    }
    return new ShellGeometryAssignments(displacements, thicknesses);
  }

  private void LoadTables()
  {
    if (_insertions is not null && _thicknesses is not null)
    {
      return;
    }
    var tables = settingsStore.Current.SapModel.DatabaseTables;
    int count = 0;
    string[] keys = [],
      names = [];
    int[] importTypes = [];
    bool[] empty = [];
    if (tables.GetAllTables(ref count, ref keys, ref names, ref importTypes, ref empty) != 0)
    {
      throw new InvalidOperationException("Failed to read shell assignment tables.");
    }
    _insertions = ReadTable(tables, INSERTION_TABLE, keys, empty);
    _thicknesses = ReadTable(tables, THICKNESS_TABLE, keys, empty);
  }

  private static Dictionary<string, List<Dictionary<string, string>>> ReadTable(
    cDatabaseTables tables,
    string table,
    string[] keys,
    bool[] empty
  )
  {
    int index = Array.IndexOf(keys, table);
    if (index < 0 || index >= empty.Length)
    {
      throw new InvalidOperationException("Shell assignment table is unavailable.");
    }
    var result = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.Ordinal);
    if (empty[index])
    {
      return result;
    }
    string[] requested = [],
      fields = [],
      data = [];
    int version = 0,
      records = 0;
    if (
      tables.GetTableForDisplayArray(table, ref requested, "", ref version, ref fields, ref records, ref data) != 0
      || !fields.Contains("UniqueName")
      || data.Length != records * fields.Length
    )
    {
      throw new InvalidOperationException("Failed to read shell assignment table rows.");
    }
    for (int rowIndex = 0; rowIndex < records; rowIndex++)
    {
      var row = new Dictionary<string, string>(StringComparer.Ordinal);
      for (int column = 0; column < fields.Length; column++)
      {
        row.Add(fields[column], data[rowIndex * fields.Length + column]);
      }
      string name = row["UniqueName"];
      if (!result.TryGetValue(name, out var rows))
      {
        rows = [];
        result.Add(name, rows);
      }
      rows.Add(row);
    }
    return result;
  }

  private static int Corner(IReadOnlyDictionary<string, string> row, int count)
  {
    if (
      !int.TryParse(row["PointNumber"], NumberStyles.Integer, CultureInfo.InvariantCulture, out int point)
      || point < 1
      || point > count
    )
    {
      throw new InvalidOperationException("Invalid shell assignment corner.");
    }
    return point - 1;
  }

  private static double Number(IReadOnlyDictionary<string, string> row, string field)
  {
    if (
      !double.TryParse(row[field], NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
      || double.IsNaN(value)
      || double.IsInfinity(value)
    )
    {
      throw new InvalidOperationException("Invalid shell assignment value.");
    }
    return value;
  }
}
