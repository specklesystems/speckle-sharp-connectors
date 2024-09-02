using Speckle.Converters.Common;

namespace Speckle.Converters.RevitShared.Helpers;

/// <summary>
/// POC: reference point functionality needs to be revisited (we are currently baking in these transforms into all geometry using the point and vector converters, and losing the transform).
/// This converter uses the transform in the reference point setting and provides methods to transform points
/// </summary>
public class ReferencePointConverter : IReferencePointConverter
{
  private readonly IContextStore<RevitConversionContext> _contextStack;

  public ReferencePointConverter(IContextStore<RevitConversionContext> contextStack)
  {
    _contextStack = contextStack;
  }

  public DB.XYZ ConvertToExternalCoordinates(DB.XYZ p, bool isPoint)
  {
    if (_contextStack.ToSpeckleSettings.ReferencePointTransform is DB.Transform transform)
    {
      return isPoint ? transform.Inverse.OfPoint(p) : transform.Inverse.OfVector(p);
    }

    return p;
  }

  public DB.XYZ ConvertToInternalCoordinates(DB.XYZ p, bool isPoint)
  {
    if (_contextStack.ToSpeckleSettings.ReferencePointTransform is DB.Transform transform)
    {
      return isPoint ? transform.OfPoint(p) : transform.OfVector(p);
    }

    return p;
  }
}
