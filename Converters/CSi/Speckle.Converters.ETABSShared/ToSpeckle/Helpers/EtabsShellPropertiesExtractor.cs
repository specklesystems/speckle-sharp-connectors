using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.Extensions;
using Speckle.Converters.CSiShared.ToSpeckle.Helpers;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Converters.ETABSShared.ToSpeckle.Helpers;

/// <summary>
/// Extracts ETABS-specific properties from shell elements using the AreaObj API calls.
/// </summary>
public sealed class EtabsShellPropertiesExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;
  private readonly CsiToSpeckleCacheSingleton _csiToSpeckleCacheSingleton;
  private readonly DatabaseTableExtractor _databaseTableExtractor;
  private readonly EtabsShellSectionResolver _etabsShellSectionResolver;

  public EtabsShellPropertiesExtractor(
    CsiToSpeckleCacheSingleton csiToSpeckleCacheSingleton,
    IConverterSettingsStore<CsiConversionSettings> settingsStore,
    DatabaseTableExtractor databaseTableExtractor,
    EtabsShellSectionResolver etabsShellSectionResolver
  )
  {
    _settingsStore = settingsStore;
    _csiToSpeckleCacheSingleton = csiToSpeckleCacheSingleton;
    _databaseTableExtractor = databaseTableExtractor;
    _etabsShellSectionResolver = etabsShellSectionResolver;
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

    // CNX-2725 adds more numeric props for dashboard-ing
    var geometry = properties.EnsureNested(ObjectPropertyCategory.GEOMETRY);
    double area = GetArea(shell, designOrientation);
    double thickness = GetSectionThickness(sectionId);

    double volume = double.NaN;
    if (!double.IsNaN(area) && !double.IsNaN(thickness) && area > 0 && thickness > 0)
    {
      // I am paranoid about what etabs could throw our way
      double computedVolume = area * thickness;
      volume = (!double.IsInfinity(computedVolume) && !double.IsNaN(computedVolume)) ? computedVolume : double.NaN;
    }

    geometry.AddWithUnits(ObjectPropertyKey.THICKNESS, thickness, _settingsStore.Current.SpeckleUnits);
    geometry.AddWithUnits(ObjectPropertyKey.AREA, area, $"{_settingsStore.Current.SpeckleUnits}²");
    geometry.AddWithUnits(ObjectPropertyKey.VOLUME, volume, $"{_settingsStore.Current.SpeckleUnits}³");

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
    string materialId = _databaseTableExtractor
      .GetTableData("Area Section Property Definitions - Summary", "Name", ["Name", "Material"])
      .GetRowValue(sectionName, "Material");
    return materialId;
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
    string area = _databaseTableExtractor
      .GetTableData(tableKey, requestedColumns: ["UniqueName", "Area"])
      .GetRowValue(shell.Name, "Area");

    // all database data is returned as strings
    return double.TryParse(area, out var result) ? result : double.NaN;
  }

  /// <summary>
  /// Gets section thickness, resolving and caching section properties on first encounter.
  /// </summary>
  /// <param name="sectionId">The section name to get thickness for</param>
  /// <returns>Thickness value, or NaN if section is invalid or thickness cannot be determined</returns>
  private double GetSectionThickness(string sectionId)
  {
    // Guard against invalid sections
    if (string.IsNullOrEmpty(sectionId) || sectionId == "None")
    {
      return double.NaN;
    }

    // Check if section already resolved and cached
    if (!_csiToSpeckleCacheSingleton.ShellSectionPropertiesCache.TryGetValue(sectionId, out var sectionProperties))
    {
      // First encounter - resolve section and cache all properties
      sectionProperties = _etabsShellSectionResolver.ResolveSection(sectionId);
      _csiToSpeckleCacheSingleton.ShellSectionPropertiesCache[sectionId] = sectionProperties;
    }

    // Extract thickness from cached properties
    return ExtractThicknessFromProperties(sectionProperties);
  }

  /// <summary>
  /// Extracts thickness value from resolved section properties dictionary structure.
  /// </summary>
  /// <remarks>
  /// Section properties have nested structure:
  /// { "Property Data" -> { "Thickness" -> { "value" -> double, "units" -> string } } }
  /// </remarks>
  private static double ExtractThicknessFromProperties(Dictionary<string, object?> sectionProperties)
  {
    if (!sectionProperties.TryGetValue(SectionPropertyCategory.PROPERTY_DATA, out object? propertyDataObj))
    {
      return double.NaN;
    }

    if (propertyDataObj is not Dictionary<string, object?> propertyData)
    {
      return double.NaN;
    }

    if (!propertyData.TryGetValue(ObjectPropertyKey.THICKNESS, out object? thicknessObj))
    {
      return double.NaN;
    }

    if (thicknessObj is not Dictionary<string, object> thicknessDict)
    {
      return double.NaN;
    }

    if (!thicknessDict.TryGetValue("value", out object? valueObj))
    {
      return double.NaN;
    }

    return valueObj is double thickness ? thickness : double.NaN;
  }
}
