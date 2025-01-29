using Speckle.Converters.Common;
using static Speckle.Converters.Common.Result;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class PointToSpeckleRawConverter(IConverterSettingsStore<AutocadConversionSettings> settingsStore)
  : ITypedConverter<AG.Point3d, SOG.Point>
{
  public Result<SOG.Point> Convert(AG.Point3d target) =>
    Success<SOG.Point>(new(target.X, target.Y, target.Z, settingsStore.Current.SpeckleUnits));
}
