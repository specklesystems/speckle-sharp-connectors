using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Plant3dShared.ToSpeckle.Geometry;

[NameAndRankValue(typeof(PP.PnP3dObjects.Pipe), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class PipeToSpeckleConverter(
  ITypedConverter<ABR.Brep, SOG.Mesh> brepConverter,
  IConverterSettingsStore<Plant3dConversionSettings> settingsStore
) : Plant3dEntityToSpeckleConverter(brepConverter, settingsStore);
