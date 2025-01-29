using Speckle.Converters.Common;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class VectorToSpeckleRawConverter(IConverterSettingsStore<AutocadConversionSettings> settingsStore)
  : ITypedConverter<AG.Vector3d, SOG.Vector>
{
  public Result<SOG.Vector> Convert(AG.Vector3d target) =>
    Result.Success<SOG.Vector>(new(target.X, target.Y, target.Z, settingsStore.Current.SpeckleUnits));
}
