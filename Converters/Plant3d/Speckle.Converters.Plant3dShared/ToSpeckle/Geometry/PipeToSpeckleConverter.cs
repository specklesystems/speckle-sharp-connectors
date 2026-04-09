using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;

namespace Speckle.Converters.Plant3dShared.ToSpeckle.Geometry;

[NameAndRankValue(typeof(PP.PnP3dObjects.Pipe), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class PipeToSpeckleConverter(
  IConverterManager<IToSpeckleTopLevelConverter> converterManager,
  IConverterSettingsStore<Plant3dConversionSettings> settingsStore
) : Plant3dEntityToSpeckleConverter(converterManager, settingsStore);
