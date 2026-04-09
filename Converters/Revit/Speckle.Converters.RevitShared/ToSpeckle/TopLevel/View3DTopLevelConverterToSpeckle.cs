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
    if (!target.IsPerspective)
    {
      throw new ConversionException("Non-Perspective views are not supported");
    }

    // some views have null origin, not sure why
    if (target.Origin == null)
    {
      throw new ConversionException("Views with no origin are not supported");
    }

    DB.ViewOrientation3D orientation = target.GetSavedOrientation();

    return new()
    {
      name = target.Name,
      position = _xyzToPointConverter.Convert(target.Origin),
      forward = _xyzToVectorConverter.Convert(orientation.ForwardDirection),
      up = _xyzToVectorConverter.Convert(orientation.UpDirection),
    };
  }
}
