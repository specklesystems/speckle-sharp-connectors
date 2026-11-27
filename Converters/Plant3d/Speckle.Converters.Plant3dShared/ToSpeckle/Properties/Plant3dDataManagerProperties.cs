using System.Diagnostics.CodeAnalysis;

namespace Speckle.Converters.Plant3dShared.ToSpeckle;

/// <summary>
/// Defines the format of the Plant3D "Data Manager" property section.
/// Written by the converter (<see cref="Plant3dDataExtractor"/>) and read by the connector when building relations.
/// All key strings for this section must be referenced from here.
/// </summary>
public static class Plant3dDataManagerProperties
{
  public const string PID_KEY = "P&ID";
  public const string PLANT3D_DATA_KEY = "Plant3D Data";
  private static readonly string[] s_plant3dSectionKeys = [PID_KEY, PLANT3D_DATA_KEY];

  private const string DATA_MANAGER_KEY = "Data Manager";

  private const string CATEGORY_KEY = "Category";
  private const string LEVEL_PREFIX = "Level";

  private const string GROUP_ID_KEY = "Group Id";
  private const string GROUP_TYPE_KEY = "Group Type";
  private const string LINE_NUMBER_KEY = "Line Number";
  private const string GROUP_TAG_KEY = "Group Tag";
  private const string GROUP_NOMINAL_SIZE_KEY = "Group Nominal Size";
  private const string GROUP_NOMINAL_SPEC_KEY = "Group Nominal Spec";

  // ── Write (converter) ──

  public static Dictionary<string, object> CreateDataManagerSection(Dictionary<string, object?> plant3dSection)
  {
    var dataManagerSection = new Dictionary<string, object>();
    plant3dSection[DATA_MANAGER_KEY] = dataManagerSection;
    return dataManagerSection;
  }

  public static void WriteCategory(
    IDictionary<string, object> dataManagerSection,
    string category,
    IReadOnlyList<string> levels
  )
  {
    dataManagerSection[CATEGORY_KEY] = category;
    for (int i = 0; i < levels.Count; i++)
    {
      dataManagerSection[$"{LEVEL_PREFIX} {i + 1}"] = levels[i];
    }
  }

  public static void WriteLineGroup(IDictionary<string, object> dataManagerSection, Plant3dLineGroup group)
  {
    dataManagerSection[GROUP_ID_KEY] = group.Id;
    dataManagerSection[GROUP_TYPE_KEY] = group.Type;
    dataManagerSection[LINE_NUMBER_KEY] = group.LineNumber;

    if (group.Tag is not null)
    {
      dataManagerSection[GROUP_TAG_KEY] = group.Tag;
    }

    if (group.NominalSize is not null)
    {
      dataManagerSection[GROUP_NOMINAL_SIZE_KEY] = group.NominalSize;
    }

    if (group.NominalSpec is not null)
    {
      dataManagerSection[GROUP_NOMINAL_SPEC_KEY] = group.NominalSpec;
    }
  }

  // ── Read (connector) ──

  /// <summary>
  /// Reads the line group written by <see cref="WriteLineGroup"/> from an object's properties,
  /// looking under either the <see cref="PID_KEY"/> or <see cref="PLANT3D_DATA_KEY"/> section.
  /// </summary>
  public static bool TryReadLineGroup(
    IReadOnlyDictionary<string, object?> objectProperties,
    [NotNullWhen(true)] out Plant3dLineGroup? group
  )
  {
    group = null;
    return TryGetDataManagerSection(objectProperties, out var dataManagerSection)
      && TryParseLineGroup(dataManagerSection, out group);
  }

  // ── Private helpers ──

  private static bool TryGetDataManagerSection(
    IReadOnlyDictionary<string, object?> objectProperties,
    [NotNullWhen(true)] out IReadOnlyDictionary<string, object?>? dataManagerSection
  )
  {
    foreach (var plant3dKey in s_plant3dSectionKeys)
    {
      if (
        TryGetDictionary(objectProperties, plant3dKey, out var plant3dSection)
        && TryGetDictionary(plant3dSection, DATA_MANAGER_KEY, out var value)
      )
      {
        dataManagerSection = value;
        return true;
      }
    }

    dataManagerSection = null;
    return false;
  }

  private static bool TryParseLineGroup(
    IReadOnlyDictionary<string, object?> dataManagerSection,
    [NotNullWhen(true)] out Plant3dLineGroup? group
  )
  {
    if (
      !dataManagerSection.TryGetValue(GROUP_ID_KEY, out var gid)
      || gid is not int id
      || id <= 0
      || !dataManagerSection.TryGetValue(GROUP_TYPE_KEY, out var gt)
      || gt is not string type
      || string.IsNullOrWhiteSpace(type)
    )
    {
      group = null;
      return false;
    }

    var lineNumber = dataManagerSection.GetValueOrDefault(LINE_NUMBER_KEY)?.ToString() ?? "";
    var tag = dataManagerSection.GetValueOrDefault(GROUP_TAG_KEY)?.ToString();
    var nominalSize = dataManagerSection.GetValueOrDefault(GROUP_NOMINAL_SIZE_KEY)?.ToString();
    var nominalSpec = dataManagerSection.GetValueOrDefault(GROUP_NOMINAL_SPEC_KEY)?.ToString();
    group = new Plant3dLineGroup(id, type, lineNumber, tag, nominalSize, nominalSpec);
    return true;
  }

  private static bool TryGetDictionary(
    IReadOnlyDictionary<string, object?> properties,
    string key,
    [NotNullWhen(true)] out IReadOnlyDictionary<string, object?>? dictionary
  )
  {
    dictionary = properties.TryGetValue(key, out var value) ? value as IReadOnlyDictionary<string, object?> : null;
    return dictionary is not null;
  }
}
