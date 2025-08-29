using Speckle.Converters.Common;
using Speckle.Converters.CSiShared.Extensions;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

/// <summary>
/// Extracts properties common to frame elements across CSi products (e.g., Etabs, Sap2000)
/// using the FrameObj API calls.
/// </summary>
/// <remarks>
/// Design Decisions:
/// <list type="bullet">
///     <item>
///         <description>
///             Individual methods preferred over batched calls due to:
///             <list type="bullet">
///                 <item><description>Independent API calls with no performance gain from batching (?)</description></item>
///                 <item><description>Easier debugging and error tracing</description></item>
///                 <item><description>Simpler maintenance as each method maps to one API concept</description></item>
///             </list>
///         </description>
///     </item>
/// </list>
/// </remarks>
public sealed class CsiFramePropertiesExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;
  private readonly CsiToSpeckleCacheSingleton _csiToSpeckleCacheSingleton;

  private static readonly string[] s_releaseKeys =
  [
    "Axial",
    "Shear 2 (Major)",
    "Shear 3 (Minor)",
    "Torsion",
    "Moment 22 (Minor)",
    "Moment 33 (Major)"
  ]; // Note: caching keys for better performance

  public CsiFramePropertiesExtractor(
    CsiToSpeckleCacheSingleton csiToSpeckleCacheSingleton,
    IConverterSettingsStore<CsiConversionSettings> settingsStore
  )
  {
    _csiToSpeckleCacheSingleton = csiToSpeckleCacheSingleton;
    _settingsStore = settingsStore;
  }

  public void ExtractProperties(CsiFrameWrapper frame, PropertyExtractionResult frameData)
  {
    frameData.ApplicationId = frame.GetSpeckleApplicationId(_settingsStore.Current.SapModel);

    var geometry = frameData.Properties.EnsureNested(ObjectPropertyCategory.GEOMETRY);
    (geometry["I-End Joint"], geometry["J-End Joint"]) = GetEndPointNames(frame);

    var assignments = frameData.Properties.EnsureNested(ObjectPropertyCategory.ASSIGNMENTS);
    assignments[CommonObjectProperty.GROUPS] = GetGroupAssigns(frame);
    assignments[CommonObjectProperty.MATERIAL_OVERWRITE] = GetMaterialOverwrite(frame);
    assignments[CommonObjectProperty.LOCAL_AXIS_2_ANGLE] = GetLocalAxes(frame);
    assignments[CommonObjectProperty.PROPERTY_MODIFIERS] = GetModifiers(frame);
    assignments["End Releases"] = GetReleases(frame);

    // NOTE: sectionId and materialId a "quick-fix" to enable filtering in the viewer etc.
    // Assign sectionId to variable as this will be an argument for the GetMaterialName method
    string sectionId = GetSectionName(frame);
    string materialId = GetMaterialName(sectionId);
    assignments[ObjectPropertyKey.SECTION_ID] = sectionId;
    assignments[ObjectPropertyKey.MATERIAL_ID] = materialId;

    // store the object, section, and material id relationships in their corresponding caches to be accessed by the connector
    if (!string.IsNullOrEmpty(sectionId))
    {
      if (_csiToSpeckleCacheSingleton.FrameSectionCache.TryGetValue(sectionId, out List<string>? frameIds))
      {
        frameIds.Add(frameData.ApplicationId);
      }
      else
      {
        _csiToSpeckleCacheSingleton.FrameSectionCache.Add(sectionId, [frameData.ApplicationId]);
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
          _csiToSpeckleCacheSingleton.MaterialCache.Add(materialId, [sectionId]);
        }
      }
    }
  }

  private string[] GetGroupAssigns(CsiFrameWrapper frame)
  {
    int numberGroups = 0;
    string[] groups = [];
    _ = _settingsStore.Current.SapModel.FrameObj.GetGroupAssign(frame.Name, ref numberGroups, ref groups);
    return (groups);
  }

  private Dictionary<string, object?> GetLocalAxes(CsiFrameWrapper frame)
  {
    double angle = 0;
    bool advanced = false;
    _ = _settingsStore.Current.SapModel.FrameObj.GetLocalAxes(frame.Name, ref angle, ref advanced);

    Dictionary<string, object?> resultsDictionary = [];
    resultsDictionary.AddWithUnits(CommonObjectProperty.ANGLE, angle, "Degrees");
    resultsDictionary[CommonObjectProperty.ADVANCED] = advanced.ToString();

    return resultsDictionary;
  }

  private string GetMaterialOverwrite(CsiFrameWrapper frame)
  {
    string propName = string.Empty;
    _ = _settingsStore.Current.SapModel.FrameObj.GetMaterialOverwrite(frame.Name, ref propName);
    return propName;
  }

  private Dictionary<string, double?> GetModifiers(CsiFrameWrapper frame)
  {
    double[] value = [];
    _ = _settingsStore.Current.SapModel.FrameObj.GetModifiers(frame.Name, ref value);
    return new Dictionary<string, double?>
    {
      ["Area"] = value[0],
      ["As2"] = value[1],
      ["As3"] = value[2],
      ["Torsion"] = value[3],
      ["I22"] = value[4],
      ["I33"] = value[5],
      ["Mass"] = value[6],
      ["Weight"] = value[7]
    };
  }

  private (string point1, string point2) GetEndPointNames(CsiFrameWrapper frame)
  {
    string point1 = string.Empty,
      point2 = string.Empty;
    _ = _settingsStore.Current.SapModel.FrameObj.GetPoints(frame.Name, ref point1, ref point2);
    return (point1, point2);
  }

  private Dictionary<string, object?> GetReleases(CsiFrameWrapper frame)
  {
    bool[] ii = [],
      jj = [];
    double[] startValue = [],
      endValue = [];

    _ = _settingsStore.Current.SapModel.FrameObj.GetReleases(frame.Name, ref ii, ref jj, ref startValue, ref endValue);

    return new Dictionary<string, object?>
    {
      ["End-I"] = CreateNodeReleases(ii, startValue),
      ["End-J"] = CreateNodeReleases(jj, endValue),
    };
  }

  // NOTE: Avoid duplicate dictionary creation logic for End-I and End-J in GetReleases() method
  private static Dictionary<string, object?> CreateNodeReleases(bool[] releases, double[] values) =>
    s_releaseKeys
      .Select(
        (key, i) => // for each key, we want both the key (string) and index
          new KeyValuePair<string, object?>( // for each key, create dictionary with Release and Stiffness
            key,
            new Dictionary<string, object?> { ["Release"] = releases[i], ["Stiffness"] = values[i] }
          )
      )
      .ToDictionary(x => x.Key, x => x.Value);

  private string GetSectionName(CsiFrameWrapper frame)
  {
    string sectionName = string.Empty,
      sAuto = string.Empty;
    _ = _settingsStore.Current.SapModel.FrameObj.GetSection(frame.Name, ref sectionName, ref sAuto);
    return sectionName;
  }

  // NOTE: This is a little convoluted as we aren't on the cFrameObj level, but one deeper.
  // As noted in ExtractProperties, this is just a quick-fix to get some displayable materialId parameter
  private string GetMaterialName(string sectionName)
  {
    string materialName = string.Empty;
    _ = _settingsStore.Current.SapModel.PropFrame.GetMaterial(sectionName, ref materialName);
    return materialName;
  }
}
