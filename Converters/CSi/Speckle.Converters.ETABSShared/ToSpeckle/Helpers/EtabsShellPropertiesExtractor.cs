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
/// <list type="bullet">
///     <item><description>Extracts properties only available in ETABS (e.g., Label, Level)</description></item>
///     <item><description>Complements <see cref="CsiShellPropertiesExtractor"/> by adding product-specific data</description></item>
///     <item><description>Follows same pattern of single-purpose methods for clear API mapping</description></item>
/// </list>
///
/// Design Decisions:
/// <list type="bullet">
///     <item><description>Maintains separate methods for each property following CSI API structure</description></item>
///     <item><description>Properties are organized by their functional groups (Object ID, Assignments, Design)</description></item>
/// </list>
/// </remarks>
public sealed class EtabsShellPropertiesExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;
  private readonly CsiToSpeckleCacheSingleton _csiToSpeckleCacheSingleton;
  private readonly DatabaseTableExtractor _databaseTableExtractor;

  public EtabsShellPropertiesExtractor(
    CsiToSpeckleCacheSingleton csiToSpeckleCacheSingleton,
    IConverterSettingsStore<CsiConversionSettings> settingsStore,
    DatabaseTableExtractor databaseTableExtractor
  )
  {
    _settingsStore = settingsStore;
    _csiToSpeckleCacheSingleton = csiToSpeckleCacheSingleton;
    _databaseTableExtractor = databaseTableExtractor;
  }

  public void ExtractProperties(CsiShellWrapper shell, Dictionary<string, object?> properties)
  {
    var objectId = properties.EnsureNested(ObjectPropertyCategory.OBJECT_ID);
    string designOrientation = GetDesignOrientation(shell);
    objectId[CommonObjectProperty.DESIGN_ORIENTATION] = designOrientation;
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
    string materialId = GetMaterialForSection(sectionId, designOrientation);
    assignments[ObjectPropertyKey.SECTION_ID] = sectionId;
    assignments[ObjectPropertyKey.MATERIAL_ID] = materialId;

    var geometry = properties.EnsureNested(ObjectPropertyCategory.GEOMETRY);
    double area = GetArea(shell, designOrientation);
    geometry.AddWithUnits("Area", area, $"{_settingsStore.Current.SpeckleUnits}²");

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

  private string GetMaterialForSection(string sectionName, string designOrientation)
  {
    if (designOrientation == "Null") // openings don't have a material
    {
      return string.Empty;
    }
    var sectionRowData = _databaseTableExtractor
      .GetTableData("Area Section Property Definitions - Summary", "Name", ["Name", "Material"])
      .Rows[sectionName];
    return sectionRowData["Material"];
  }

  private double GetArea(CsiShellWrapper shell, string designOrientation)
  {
    // database to use depends on sub shell-type
    string tableKey = designOrientation switch
    {
      "Floor" => "Floor Object Connectivity",
      "Wall" => "Wall Object Connectivity",
      "Null" => "Null Area Object Connectivity",
      _ => throw new ArgumentException($"Unexpected design orientation: {designOrientation}")
    };

    // using the DatabaseTableExtractor fetch table with key from the designOrientation
    // limit query size to "UniqueName" and "Area"
    var geometricPropertiesData = _databaseTableExtractor
      .GetTableData(tableKey, requestedColumns: ["UniqueName", "Area"])
      .Rows[shell.Name];

    // all database data is returned as strings
    return double.TryParse(geometricPropertiesData["Area"], out var result) ? result : double.NaN;
  }
}
