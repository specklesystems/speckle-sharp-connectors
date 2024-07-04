using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Core.Kits;
using Speckle.Core.Models;

namespace Speckle.Converters.ArcGIS3.ToHost.Raw;

public class PointToHostConverter : ITypedConverter<SOG.Point, ACG.MapPoint>
{
  private readonly IConversionContextStack<ArcGISDocument, ACG.Unit> _contextStack;

  public PointToHostConverter(IConversionContextStack<ArcGISDocument, ACG.Unit> contextStack)
  {
    _contextStack = contextStack;
  }

  public object Convert(Base target) => Convert((SOG.Point)target);

  public ACG.MapPoint Convert(SOG.Point target)
  {
    double scaleFactor = Units.GetConversionFactor(target.units, _contextStack.Current.SpeckleUnits);
    return new ACG.MapPointBuilderEx(
      target.x * scaleFactor,
      target.y * scaleFactor,
      target.z * scaleFactor,
      _contextStack.Current.Document.Map.SpatialReference
    ).ToGeometry();
  }
}
