using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;

namespace Speckle.Converters.Plant3dShared.ToSpeckle.Geometry;

/// <summary>
/// Converts inline pipe assets (valves, instruments, etc.) to Speckle.
/// </summary>
[NameAndRankValue(typeof(PP.PnP3dObjects.PipeInlineAsset), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class PipeInlineAssetToSpeckleConverter(
  IConverterManager<IToSpeckleTopLevelConverter> converterManager,
  IConverterSettingsStore<Plant3dConversionSettings> settingsStore
) : Plant3dEntityToSpeckleConverter(converterManager, settingsStore);
