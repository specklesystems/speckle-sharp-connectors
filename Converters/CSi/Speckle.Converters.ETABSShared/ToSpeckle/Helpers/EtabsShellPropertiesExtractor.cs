using System.Collections.Concurrent;
using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.Extensions;
using Speckle.Converters.CSiShared.ToSpeckle.Helpers;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Converters.ETABSShared.ToSpeckle.Helpers;

/// <summary>
/// Extracts ETABS-specific properties from shell elements using the AreaObj API calls.
/// </summary>
/// <remarks>
/// Responsibilities:
/// - Extracts properties only available in ETABS (e.g., Label, Level)
/// - Complements <see cref="CsiShellPropertiesExtractor"/> by adding product-specific data
/// - Follows same pattern of single-purpose methods for clear API mapping
///
/// Design Decisions:
/// - Maintains separate methods for each property following CSI API structure
/// - Properties are organized by their functional groups (Object ID, Assignments, Design)
///
/// Integration:
/// - Used by <see cref="EtabsPropertiesExtractor"/> for shell-specific property extraction
/// - Works alongside CsiShellPropertiesExtractor to build complete property set
/// </remarks>
public sealed class EtabsShellPropertiesExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;
  private readonly MaterialNameLookup _materialNameLookup;
  private readonly CsiToSpeckleCacheSingleton _csiToSpeckleCacheSingleton;

  public EtabsShellPropertiesExtractor(
    CsiToSpeckleCacheSingleton csiToSpeckleCacheSingleton,
    IConverterSettingsStore<CsiConversionSettings> settingsStore
  )
  {
    _settingsStore = settingsStore;
    _materialNameLookup = new MaterialNameLookup(settingsStore);
    _csiToSpeckleCacheSingleton = csiToSpeckleCacheSingleton;
  }

  public void ExtractProperties(CsiShellWrapper shell, Dictionary<string, object?> properties)
  {
    var objectId = properties.EnsureNested(ObjectPropertyCategory.OBJECT_ID);
    objectId["Design Orientation"] = GetDesignOrientation(shell);
    (objectId[CommonObjectProperty.LABEL], objectId[CommonObjectProperty.LEVEL]) = GetLabelAndLevel(shell);

    var assignments = properties.EnsureNested(ObjectPropertyCategory.ASSIGNMENTS);
    assignments["Diaphragm"] = GetAssignedDiaphragmName(shell);
    assignments["Opening"] = IsOpening(shell);
    assignments["Pier"] = GetPierAssignmentName(shell);
    assignments["Spandrel"] = GetSpandrelAssignmentName(shell);
    assignments[CommonObjectProperty.SPRING_ASSIGNMENT] = GetSpringAssignmentName(shell);

    // NOTE: Section Property and Material are a "quick-fix" to enable filtering in the viewer etc.
    // Assign Section Property to variable as this will be an argument for the GetMaterialName method
    string shellAppId = shell.GetSpeckleApplicationId(_settingsStore.Current.SapModel);
    string sectionId = GetSectionName(shell);
    string materialId = _materialNameLookup.GetMaterialForSection(sectionId);
    assignments[ObjectPropertyKey.SECTION_ID] = sectionId;
    assignments[ObjectPropertyKey.MATERIAL_ID] = materialId;

    // store the object, section, and material id relationships in their corresponding caches to be accessed by the connector
    if (!string.IsNullOrEmpty(sectionId))
    {
      if (_csiToSpeckleCacheSingleton.ShellSectionCache.TryGetValue(sectionId, out List<string>? shellIds))
      {
        shellIds.Add(shellAppId);
      }
      else
      {
        _csiToSpeckleCacheSingleton.ShellSectionCache.Add(sectionId, new List<string>() { shellAppId });
      }

      if (!string.IsNullOrEmpty(materialId))
      {
        if (_csiToSpeckleCacheSingleton.MaterialCache.TryGetValue(materialId, out List<string>? sectionIds))
        {
          // Since this is happening on the object level, we could be processing the same sectionIds (from different
          // objects) many times. This is not necessary since we just want a set of sectionId corresponding to material
          if (!sectionIds.Contains(sectionId))
          {
            sectionIds.Add(sectionId);
          }
        }
        else
        {
          _csiToSpeckleCacheSingleton.MaterialCache.Add(materialId, new List<string>() { sectionId });
        }
      }
    }
  }

  private (string label, string level) GetLabelAndLevel(CsiShellWrapper shell)
  {
    string label = string.Empty,
      level = string.Empty;
    _ = _settingsStore.Current.SapModel.AreaObj.GetLabelFromName(shell.Name, ref label, ref level);
    return (label, level);
  }

  private string GetDesignOrientation(CsiShellWrapper shell)
  {
    eAreaDesignOrientation designOrientation = eAreaDesignOrientation.Null;
    _ = _settingsStore.Current.SapModel.AreaObj.GetDesignOrientation(shell.Name, ref designOrientation);
    return designOrientation.ToString();
  }

  private string GetAssignedDiaphragmName(CsiShellWrapper shell)
  {
    string diaphragmName = string.Empty;
    _ = _settingsStore.Current.SapModel.AreaObj.GetDiaphragm(shell.Name, ref diaphragmName);
    return diaphragmName;
  }

  private string IsOpening(CsiShellWrapper shell)
  {
    bool isOpening = false;
    _ = _settingsStore.Current.SapModel.AreaObj.GetOpening(shell.Name, ref isOpening);
    return isOpening.ToString();
  }

  private string GetPierAssignmentName(CsiShellWrapper shell)
  {
    string pierAssignment = string.Empty;
    _ = _settingsStore.Current.SapModel.AreaObj.GetPier(shell.Name, ref pierAssignment);
    return pierAssignment;
  }

  private string GetSpandrelAssignmentName(CsiShellWrapper shell)
  {
    string spandrelAssignment = string.Empty;
    _ = _settingsStore.Current.SapModel.AreaObj.GetSpandrel(shell.Name, ref spandrelAssignment);
    return spandrelAssignment;
  }

  private string GetSpringAssignmentName(CsiShellWrapper shell)
  {
    string springAssignmentName = string.Empty;
    _ = _settingsStore.Current.SapModel.AreaObj.GetSpringAssignment(shell.Name, ref springAssignmentName);
    return springAssignmentName;
  }

  // NOTE: Moved from CsiShellPropertiesExtractor because of the materialId issue.
  // Results of the cDatabaseTable query for "Area Section Property Definitions - Summary" vary between Sap and Etabs
  private string GetSectionName(CsiShellWrapper shell)
  {
    string sectionName = string.Empty;
    _ = _settingsStore.Current.SapModel.AreaObj.GetProperty(shell.Name, ref sectionName);
    return sectionName;
  }

  // TODO: This is a temporary solution until proper DatabaseTables implementation is available.
  // FrameObj can use the following query: PropFrame.GetMaterial
  // AreaObj doesn't have a PropArea.GetMaterial method
  // So, what to do? Simplest solution: query the cDatabaseTable for the summary of area sections
  // Cache the results as a dictionary where keys are sectionName and values are materialId
  // Use the cached result to return the material string given a section name
  // This is a temporary solution! The use of cDatabaseTable are being explored as a way to simplify a lot moving forward
  private sealed class MaterialNameLookup
  {
    private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;
    private readonly ConcurrentDictionary<string, string> _materialLookup = new();
    private bool _isInitialized;

    public MaterialNameLookup(IConverterSettingsStore<CsiConversionSettings> settingsStore)
    {
      _settingsStore = settingsStore;
    }

    public string GetMaterialForSection(string sectionName)
    {
      if (!_isInitialized)
      {
        InitializeCache();
      }

      return _materialLookup.TryGetValue(sectionName, out string? value) ? value : string.Empty;
    }

    private void InitializeCache()
    {
      string[] fieldKeyList = [],
        fieldKeysIncluded = [],
        tableData = [];
      int tableVersion = 0,
        numberOfRecords = 0;

      int result = _settingsStore.Current.SapModel.DatabaseTables.GetTableForDisplayArray(
        "Area Section Property Definitions - Summary",
        ref fieldKeyList,
        "",
        ref tableVersion,
        ref fieldKeysIncluded,
        ref numberOfRecords,
        ref tableData
      );

      if (result != 0 || numberOfRecords == 0)
      {
        _isInitialized = true; // Mark as initialized even on failure
        return;
      }

      // Process each record (each record has fieldKeysIncluded.Length columns)
      for (int i = 0; i < tableData.Length; i += fieldKeysIncluded.Length)
      {
        string name = tableData[i]; // Name is first column
        string material = tableData[i + 3]; // Material is fourth column

        if (!string.IsNullOrEmpty(name))
        {
          _materialLookup.TryAdd(name, material);
        }
      }

      _isInitialized = true;
    }
  }
}
