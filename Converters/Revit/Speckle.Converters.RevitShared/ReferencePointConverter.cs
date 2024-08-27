namespace Speckle.Converters.RevitShared.Helpers;

/// <summary>
/// POC: reference point functionality needs to be revisited (we are currently baking in these transforms into all geometry using the point and vector converters, and losing the transform).
/// This converter stores the selected reference point setting value and provides a method to get out the transform for this reference point.
/// POC: uses context stack current doc on instantiation and assumes context stack doc won't change. Linked docs will NOT be supported atm.
/// </summary>
public class ReferencePointConverter : IReferencePointConverter
{
  private readonly IRevitConversionContextStack _contextStack;

  public ReferencePointConverter(IRevitConversionContextStack contextStack)
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
