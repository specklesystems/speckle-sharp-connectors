using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Plant3dShared.ToSpeckle.Geometry;

/// <summary>
/// Converts inline pipe assets (valves, instruments, etc.) to Speckle.
/// </summary>
[NameAndRankValue(typeof(PP.PnP3dObjects.PipeInlineAsset), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class PipeInlineAssetToSpeckleConverter(
  ITypedConverter<ABR.Brep, SOG.Mesh> brepConverter,
  IConverterSettingsStore<Plant3dConversionSettings> settingsStore
) : Plant3dEntityToSpeckleConverter(brepConverter, settingsStore);
