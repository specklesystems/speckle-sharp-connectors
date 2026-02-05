using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.ToSpeckle.Helpers;

namespace Speckle.Connectors.ETABSShared.HostApp.Services;

/// <summary>
/// Loads and caches section property definitions from database tables for both frame and shell sections.
/// </summary>
public class EtabsSectionPropertyDefinitionService
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;
  public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> FrameDefinitions { get; }
  public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ShellDefinitions { get; }

  public EtabsSectionPropertyDefinitionService(
    DatabaseTableExtractor databaseTableExtractor,
    IConverterSettingsStore<CsiConversionSettings> settingsStore
  )
  {
    _settingsStore = settingsStore;

    var availableTableKeys = GetAvailableTableKeys();

    FrameDefinitions = LoadFrameDefinitions(databaseTableExtractor, availableTableKeys);
    ShellDefinitions = LoadShellDefinitions(databaseTableExtractor, availableTableKeys);
  }

  private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> LoadFrameDefinitions(
    DatabaseTableExtractor databaseTableExtractor,
    string[] availableTableKeys
  )
  {
    var frameTableKeys = GetFrameSectionPropertyDefinitionTableKeys(availableTableKeys);
    return LoadDefinitionsFromTables(databaseTableExtractor, frameTableKeys);
  }

  private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> LoadShellDefinitions(
    DatabaseTableExtractor databaseTableExtractor,
    string[] availableTableKeys
  )
  {
    var shellTableKeys = GetShellSectionPropertyDefinitionTableKeys(availableTableKeys);
    return LoadDefinitionsFromTables(databaseTableExtractor, shellTableKeys);
  }

  private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> LoadDefinitionsFromTables(
    DatabaseTableExtractor databaseTableExtractor,
    IEnumerable<string> tableKeys
  )
  {
    var definitions = new Dictionary<string, IReadOnlyDictionary<string, string>>();

    foreach (string tableKey in tableKeys)
    {
      var tableData = databaseTableExtractor.GetTableData(tableKey, "Name");
      foreach (var row in tableData.Rows)
      {
        definitions[row.Key] = row.Value;
      }
    }

    return definitions;
  }

  private static IEnumerable<string> GetFrameSectionPropertyDefinitionTableKeys(string[] availableTableKeys)
  {
    var keysToExclude = new HashSet<string>
    {
      "Frame Section Property Definitions - Summary",
      "Frame Section Property Definitions - Concrete Beam Reinforcing",
      "Frame Section Property Definitions - Concrete Column Reinforcing"
    };

    return availableTableKeys.Where(key =>
      key.StartsWith("Frame Section Property Definitions") && !keysToExclude.Contains(key)
    );
  }

  private static IEnumerable<string> GetShellSectionPropertyDefinitionTableKeys(string[] availableTableKeys)
  {
    var keysToExclude = new HashSet<string> { "Area Section Property Definitions - Summary" };

    return availableTableKeys.Where(key =>
      key.StartsWith("Area Section Property Definitions") && !keysToExclude.Contains(key)
    );
  }

  private string[] GetAvailableTableKeys()
  {
    int numberTables = 0;
    string[] tableKey = [],
      tableName = [];
    int[] importType = [];

    _ = _settingsStore.Current.SapModel.DatabaseTables.GetAvailableTables(
      ref numberTables,
      ref tableKey,
      ref tableName,
      ref importType
    );

    return tableKey;
  }
}
