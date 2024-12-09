using Speckle.Converters.Common;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

/// <summary>
/// Extracts properties common to frame elements across CSi products (e.g., ETABS, SAP2000)
/// using the FrameObj API calls.
/// </summary>
/// <remarks>
/// Responsibilities:
/// - Provides a focused interface for extracting properties specific to frame elements.
/// - Ensures consistency in property extraction logic across supported CSi products.
/// Integration:
/// - Part of the property extraction hierarchy.
/// - Used by <see cref="CsiGeneralPropertiesExtractor"/> for delegating frame property extraction.
/// </remarks>
public sealed class CsiFramePropertiesExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;

  public CsiFramePropertiesExtractor(IConverterSettingsStore<CsiConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public void ExtractProperties(CsiFrameWrapper frame, Dictionary<string, object?> properties)
  {
    properties["applicationId"] = GetApplicationId(frame);

    var geometry = DictionaryUtils.EnsureNestedDictionary(properties, "geometry");
    (geometry["startJointName"], geometry["endJointName"]) = GetEndPointNames(frame);

    var assignments = DictionaryUtils.EnsureNestedDictionary(properties, "assignments");
    assignments["groups"] = new List<string>(GetGroupAssigns(frame));
    assignments["localAxes"] = GetLocalAxes(frame);
    assignments["materialOverwrite"] = GetMaterialOverwrite(frame);
    assignments["modifiers"] = GetModifiers(frame);
    assignments["endReleases"] = GetReleases(frame);
  }

  private string GetApplicationId(CsiFrameWrapper frame)
  {
    string applicationId = string.Empty;
    _ = _settingsStore.Current.SapModel.FrameObj.GetGUID(frame.Name, ref applicationId);
    return applicationId;
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
    string propName = string.Empty;
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
      ["massModifier"] = value[6],
      ["weightModifier"] = value[7]
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
    bool[] ii = Array.Empty<bool>(),
      jj = Array.Empty<bool>();
    double[] startValue = Array.Empty<double>(),
      endValue = Array.Empty<double>();

    _ = _settingsStore.Current.SapModel.FrameObj.GetReleases(frame.Name, ref ii, ref jj, ref startValue, ref endValue);

    var releaseKeys = new[] { "axial", "minorShear", "majorShear", "torsion", "minorBending", "majorBending" };

    var startNodes = releaseKeys
      .Select(
        (key, index) =>
          new KeyValuePair<string, object?>(
            $"{key}StartNode",
            new Dictionary<string, object?> { ["release"] = ii[index], ["stiffness"] = startValue[index] }
          )
      )
      .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

    var endNodes = releaseKeys
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
}

/*
GetDesignProcedure - Common but returns are different -> Etabs - Done
GetElm - Backlog (Important? Can be user-requested)
GetEndLengthOffset - Backlog (Important? Can be user-requested)
GetGroupAssign - Done
GetGUID - Done
GetInsertionPoint_1 - Backlog (Important? Can be user-requested)
GetLateralBracing - Backlog (Important? Can be user-requested)
GetLoadDistributed - Backlog (Nothing in place for loads, yet)
GetLoadPoint - Backlog (Nothing in place for loads, yet)
GetLoadTemperature - Backlog (Nothing in place for loads, yet)
GetLocalAxes - Done
GetMass - Backlog (Important? Can be user-requested)
GetMaterialOverwrite - Done
GetModifiers - Done
GetNameList - Backlog (Important? Can be user-requested)
GetOutputStations - Backlog (Important? Can be user-requested)
GetPoints - Done
GetReleases - Done
GetSection
GetSectionNonPrismatic
GetSelected - Don't need this. Won't do.
GetTCLimits
GetTransformationMatrix
GetTypeOAPI
 */
