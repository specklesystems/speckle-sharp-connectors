using Speckle.Converters.CSiShared.Extensions;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

/// <summary>
/// Provides extension methods for processing raw analysis results data into structured
/// hierarchical format.
/// </summary>
public static class AnalysisResultsProcessor
{
  /// <summary>
  /// Transforms raw table data into a hierarchical structure organized by structural element,
  /// load case, and station with force components as values.
  /// </summary>
  /// <remarks>
  /// This implementation uses a hardcoded hierarchical structure rather than an algorithmic approach
  /// for several reasons:
  /// 1. Performance - avoid expensive data analysis operations
  /// 2. Predictability - provides consistent structure regardless of data patterns
  /// 3. Domain Appropriateness - Structure reflects the physical/logical relationships in structural analysis:
  ///    - CaseType is always a property of OutputCase
  ///    - Force components (P, V2, M3, etc.) are always properties of Station
  ///
  /// The method still dynamically adapts to the presence/absence of specific force components.
  /// </remarks>
  /// <param name="tableData">The raw table data from DatabaseTableExtractor</param>
  /// <returns>A hierarchical dictionary structure of forces organized by element, load case, and station</returns>
  public static Dictionary<string, Dictionary<string, object>> AsHierarchicalForces(this TableData tableData)
  {
    // Define known hierarchy levels
    const string ELEMENT_LEVEL = "UniqueName";
    const string CASE_LEVEL = "OutputCase";
    const string STATION_LEVEL = "Station";

    // Define attributes known to belong to the case level
    string[] caseAttributes = ["CaseType"]; // TODO: POC fit for purpose for column forces. Need to make more flexible

    // Known possible force components
    string[] knownForceComponents = ["P", "V2", "V3", "T", "M2", "M3"]; // TODO: POC fit for purpose for column forces. Need to make more flexible

    // Determine which force components are available in the data
    var sampleRow = tableData.Rows.Values.FirstOrDefault();
    if (sampleRow == null)
    {
      return new Dictionary<string, Dictionary<string, object>>();
    }

    var availableForceComponents = sampleRow.Keys.Intersect(knownForceComponents).ToArray();

    // Choose appropriate processing method based on key type
    return tableData.HasCompoundKeys
      ? ProcessCompoundKeyData(tableData, caseAttributes, availableForceComponents)
      : ProcessSimpleKeyData(
        tableData,
        ELEMENT_LEVEL,
        CASE_LEVEL,
        STATION_LEVEL,
        caseAttributes,
        availableForceComponents
      );
  }

  /// <summary>
  /// Process table data that uses compound keys (UniqueName|OutputCase|Station)
  /// </summary>
  private static Dictionary<string, Dictionary<string, object>> ProcessCompoundKeyData(
    TableData tableData,
    string[] caseAttributes,
    string[] availableForceComponents
  )
  {
    var result = new Dictionary<string, Dictionary<string, object>>();
    var rowsByElement = new Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<string, string>>>>();

    foreach (var entry in tableData.Rows)
    {
      // Parse the compound key (format: UniqueName|OutputCase|Station)
      string[] keyParts = entry.Key.Split('|');
      if (keyParts.Length < 3)
      {
        continue;
      }

      string element = keyParts[0];
      string outputCase = keyParts[1];
      string station = keyParts[2];
      var rowData = entry.Value;

      // Ensure dictionaries exist for this hierarchy
      if (!rowsByElement.TryGetValue(element, out var caseDict))
      {
        caseDict = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();
        rowsByElement[element] = caseDict;
      }

      if (!caseDict.TryGetValue(outputCase, out var stationDict))
      {
        stationDict = new Dictionary<string, Dictionary<string, string>>();
        caseDict[outputCase] = stationDict;
      }

      // Extract force values
      var forces = new Dictionary<string, string>();
      foreach (var component in availableForceComponents)
      {
        if (rowData.TryGetValue(component, out var value))
        {
          forces[component] = value;
        }
      }

      stationDict[station] = forces;
    }

    // Now construct the result structure
    foreach (var elementEntry in rowsByElement)
    {
      string element = elementEntry.Key;
      var casesDict = new Dictionary<string, object>();

      foreach (var caseEntry in elementEntry.Value)
      {
        string outputCase = caseEntry.Key;
        var caseData = new Dictionary<string, object>();

        // Find CaseType for this OutputCase
        foreach (var stationEntry in caseEntry.Value)
        {
          // Get the first row for this case to extract case attributes
          var firstKey = $"{element}|{outputCase}|{stationEntry.Key}";
          if (tableData.Rows.TryGetValue(firstKey, out var rowData))
          {
            // Add case attributes
            foreach (var attribute in caseAttributes)
            {
              if (rowData.TryGetValue(attribute, out var value))
              {
                caseData[attribute] = value;
              }
            }
            break; // Only need to do this once per case
          }
        }

        // Add stations
        caseData["Stations"] = caseEntry.Value;
        casesDict[outputCase] = caseData;
      }

      result[element] = casesDict;
    }

    return result;
  }

  /// <summary>
  /// Process table data that uses simple keys (just UniqueName)
  /// </summary>
  private static Dictionary<string, Dictionary<string, object>> ProcessSimpleKeyData(
    TableData tableData,
    string elementLevel,
    string caseLevel,
    string stationLevel,
    string[] caseAttributes,
    string[] availableForceComponents
  )
  {
    var result = new Dictionary<string, Dictionary<string, object>>();

    // Group elements first
    var elementGroups = tableData
      .Rows.Values.GroupBy(row => row.TryGetValue(elementLevel, out var val) ? val : string.Empty)
      .Where(g => !string.IsNullOrEmpty(g.Key));

    foreach (var elementGroup in elementGroups)
    {
      string element = elementGroup.Key;
      var casesDict = new Dictionary<string, object>();

      // Group by OutputCase
      var caseGroups = elementGroup
        .GroupBy(row => row.TryGetValue(caseLevel, out var val) ? val : string.Empty)
        .Where(g => !string.IsNullOrEmpty(g.Key));

      foreach (var caseGroup in caseGroups)
      {
        string outputCase = caseGroup.Key;
        var caseData = new Dictionary<string, object>();

        // Add case attributes
        var firstRow = caseGroup.First();
        foreach (var attribute in caseAttributes)
        {
          if (firstRow.TryGetValue(attribute, out var value))
          {
            caseData[attribute] = value;
          }
        }

        // Group by Station
        var stationsDict = new Dictionary<string, Dictionary<string, string>>();
        var stationGroups = caseGroup
          .GroupBy(row => row.TryGetValue(stationLevel, out var val) ? val : string.Empty)
          .Where(g => !string.IsNullOrEmpty(g.Key));

        foreach (var stationGroup in stationGroups)
        {
          string station = stationGroup.Key;
          var row = stationGroup.First();

          // Extract force values
          var forces = new Dictionary<string, string>();
          foreach (var component in availableForceComponents)
          {
            if (row.TryGetValue(component, out var value))
            {
              forces[component] = value;
            }
          }

          stationsDict[station] = forces;
        }

        caseData["Stations"] = stationsDict;
        casesDict[outputCase] = caseData;
      }

      result[element] = casesDict;
    }

    return result;
  }
}
