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
/// - Individual methods preferred over batched calls due to:
///   * Independent API calls with no performance gain from batching (?)
///   * Easier debugging and error tracing
///   * Simpler maintenance as each method maps to one API concept
/// Integration:
/// - Part of the property extraction hierarchy
/// - Used by <see cref="SharedPropertiesExtractor"/> for delegating frame property extraction
/// </remarks>
public sealed class CsiFramePropertiesExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;

  private readonly CsiToSpeckleCacheSingleton _csiToSpeckleCacheSingleton;

  private static readonly string[] s_releaseKeys =
  [
    "axial",
    "minorShear",
    "majorShear",
    "torsion",
    "minorBending",
    "majorBending"
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

    var geometry = DictionaryUtils.EnsureNestedDictionary(frameData.Properties, ObjectPropertyCategory.GEOMETRY);
    (geometry["startJointName"], geometry["endJointName"]) = GetEndPointNames(frame);

    var assignments = DictionaryUtils.EnsureNestedDictionary(frameData.Properties, ObjectPropertyCategory.ASSIGNMENTS);
    assignments["groups"] = new List<string>(GetGroupAssigns(frame));
    assignments["materialOverwrite"] = GetMaterialOverwrite(frame);
    assignments["localAxis"] = GetLocalAxes(frame);
    assignments["propertyModifiers"] = GetModifiers(frame);
    assignments["endReleases"] = GetReleases(frame);
    assignments["path"] = GetPathType(frame);

    // NOTE: sectionId and materialId a "quick-fix" to enable filtering in the viewer etc.
    // Assign sectionId to variable as this will be an argument for the GetMaterialName method
    string sectionId = GetSectionName(frame);
    string materialId = GetMaterialName(sectionId);
    assignments["sectionId"] = sectionId;
    assignments["materialId"] = materialId;

    // store the object, section, and material id relationships in their corresponding caches to be accessed by the connector
    if (!string.IsNullOrEmpty(sectionId))
    {
      if (_csiToSpeckleCacheSingleton.FrameSectionCache.TryGetValue(sectionId, out List<string>? frameIds))
      {
        frameIds.Add(frameData.ApplicationId);
      }
      else
      {
        _csiToSpeckleCacheSingleton.FrameSectionCache.Add(sectionId, new List<string>() { frameData.ApplicationId });
      }

      if (!string.IsNullOrEmpty(materialId))
      {
        if (_csiToSpeckleCacheSingleton.MaterialCache.TryGetValue(materialId, out List<string>? sectionIds))
        {
          sectionIds.Add(sectionId);
        }
        else
        {
          _csiToSpeckleCacheSingleton.MaterialCache.Add(materialId, new List<string>() { sectionId });
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
    return new Dictionary<string, object?> { ["angle"] = angle, ["advanced"] = advanced.ToString() };
  }

  private string GetMaterialOverwrite(CsiFrameWrapper frame)
  {
    string propName = "None";
    _ = _settingsStore.Current.SapModel.FrameObj.GetMaterialOverwrite(frame.Name, ref propName);
    return propName;
  }

  private Dictionary<string, double?> GetModifiers(CsiFrameWrapper frame)
  {
    double[] value = Array.Empty<double>();
    _ = _settingsStore.Current.SapModel.FrameObj.GetModifiers(frame.Name, ref value);
    return new Dictionary<string, double?>
    {
      ["crossSectionalAreaModifier"] = value[0],
      ["shearAreaInLocal2DirectionModifier"] = value[1],
      ["shearAreaInLocal3DirectionModifier"] = value[2],
      ["torsionalConstantModifier"] = value[3],
      ["momentOfInertiaAboutLocal2AxisModifier"] = value[4],
      ["momentOfInertiaAboutLocal3AxisModifier"] = value[5],
      ["mass"] = value[6],
      ["weight"] = value[7]
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

    var startNodes = s_releaseKeys
      .Select(
        (key, index) =>
          new KeyValuePair<string, object?>(
            $"{key}StartNode",
            new Dictionary<string, object?> { ["release"] = ii[index], ["stiffness"] = startValue[index] }
          )
      )
      .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

    var endNodes = s_releaseKeys
      .Select(
        (key, index) =>
          new KeyValuePair<string, object?>(
            $"{key}EndNode",
            new Dictionary<string, object?> { ["release"] = jj[index], ["stiffness"] = endValue[index] }
          )
      )
      .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

    return startNodes.Concat(endNodes).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
  }

  private string GetSectionName(CsiFrameWrapper frame)
  {
    string sectionName = string.Empty,
      sAuto = string.Empty;
    _ = _settingsStore.Current.SapModel.FrameObj.GetSection(frame.Name, ref sectionName, ref sAuto);
    return sectionName;
  }

  private string GetPathType(CsiFrameWrapper frame)
  {
    string pathType = string.Empty;
    _ = _settingsStore.Current.SapModel.FrameObj.GetTypeOAPI(frame.Name, ref pathType);
    return pathType;
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
