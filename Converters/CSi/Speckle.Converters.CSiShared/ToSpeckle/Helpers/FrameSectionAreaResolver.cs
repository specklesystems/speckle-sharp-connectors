using Speckle.Converters.Common;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

/// <summary>Host-reported cross-sectional area per frame section, resolved once per send; NaN when the API fails.</summary>
public sealed class FrameSectionAreaResolver
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;
  private readonly CsiToSpeckleCacheSingleton _cache;

  public FrameSectionAreaResolver(
    IConverterSettingsStore<CsiConversionSettings> settingsStore,
    CsiToSpeckleCacheSingleton cache
  )
  {
    _settingsStore = settingsStore;
    _cache = cache;
  }

  public double GetArea(string sectionName)
  {
    if (_cache.FrameSectionAreaCache.TryGetValue(sectionName, out double cached))
    {
      return cached;
    }

    double area = 0,
      as2 = 0,
      as3 = 0,
      torsion = 0,
      i22 = 0,
      i33 = 0,
      s22 = 0,
      s33 = 0,
      z22 = 0,
      z33 = 0,
      r22 = 0,
      r33 = 0;
    int result = _settingsStore.Current.SapModel.PropFrame.GetSectProps(
      sectionName,
      ref area,
      ref as2,
      ref as3,
      ref torsion,
      ref i22,
      ref i33,
      ref s22,
      ref s33,
      ref z22,
      ref z33,
      ref r22,
      ref r33
    );

    double validatedArea = result == 0 ? area : double.NaN;
    _cache.FrameSectionAreaCache[sectionName] = validatedArea;
    return validatedArea;
  }
}
