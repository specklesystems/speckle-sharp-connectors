using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class PointToSpeckleRawConverter : ITypedConverter<AG.Point3d, SOG.Point>
{
  private readonly IConversionContextStack<Document, ADB.UnitsValue> _contextStack;

  public PointToSpeckleRawConverter(IConversionContextStack<Document, ADB.UnitsValue> contextStack)
  {
    _contextStack = contextStack;
  }

  public SOG.Point Convert(AG.Point3d target) => new(target.X, target.Y, target.Z, _contextStack.Current.SpeckleUnits);
}
