using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Converters.RevitShared.Helpers;

/// <summary>
/// POC: reference point functionality needs to be revisited (we are currently baking in these transforms into all geometry using the point and vector converters, and losing the transform).
/// This converter uses the transform in the reference point setting and provides methods to transform points
/// </summary>
public class ReferencePointConverter(IConverterSettingsStore<RevitConversionSettings> converterSettings)
  : IReferencePointConverter
{
  public DB.XYZ ConvertToExternalCoordinates(DB.XYZ p, bool isPoint)
  {
    if (converterSettings.Current.ReferencePointTransform is DB.Transform transform)
    {
      return isPoint ? transform.Inverse.OfPoint(p) : transform.Inverse.OfVector(p);
    }

    return p;
  }

  public DB.XYZ ConvertToInternalCoordinates(DB.XYZ p, bool isPoint)
  {
    if (converterSettings.Current.ReferencePointTransform is DB.Transform transform)
    {
      return isPoint ? transform.OfPoint(p) : transform.OfVector(p);
    }

    return p;
  }
}
