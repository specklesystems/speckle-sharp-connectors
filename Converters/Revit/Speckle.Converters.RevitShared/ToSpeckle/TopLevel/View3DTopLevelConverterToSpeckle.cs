using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Other;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.RevitShared.ToSpeckle;

[NameAndRankValue(typeof(DB.View3D), 0)]
public class View3DTopLevelConverterToSpeckle : IToSpeckleTopLevelConverter, ITypedConverter<DB.View3D, Camera>
{
  private readonly ITypedConverter<DB.XYZ, SOG.Point> _xyzToPointConverter;
  private readonly ITypedConverter<DB.XYZ, SOG.Vector> _xyzToVectorConverter;

  public View3DTopLevelConverterToSpeckle(
    ITypedConverter<DB.XYZ, SOG.Point> xyzToPointConverter,
    ITypedConverter<DB.XYZ, SOG.Vector> xyzToVectorConverter
  )
  {
    _xyzToPointConverter = xyzToPointConverter;
    _xyzToVectorConverter = xyzToVectorConverter;
  }

  public Base Convert(object target) => Convert((DB.View3D)target);

  public Camera Convert(DB.View3D target)
  {
    // some views have null origin, not sure why
    if (target.Origin == null)
    {
      throw new ConversionException("Views with no origin are not supported");
    }

    return new()
    {
      position = _xyzToPointConverter.Convert(target.Origin),
      forward = _xyzToVectorConverter.Convert(target.GetSavedOrientation().ForwardDirection),
      up = _xyzToVectorConverter.Convert(target.GetSavedOrientation().UpDirection),
      isOrthographic = !target.IsPerspective
    };
  }
}
