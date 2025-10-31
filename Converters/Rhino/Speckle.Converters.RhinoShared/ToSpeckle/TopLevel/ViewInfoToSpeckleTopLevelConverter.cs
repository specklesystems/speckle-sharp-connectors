using Rhino.DocObjects;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Rhino.ToSpeckle.TopLevel;

[NameAndRankValue(typeof(ViewInfo), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class ViewInfoToSpeckleTopLevelConverter : IToSpeckleTopLevelConverter, ITypedConverter<ViewInfo, SOO.Camera>
{
  private readonly ITypedConverter<RG.Point3d, SOG.Point> _pointConverter;
  private readonly ITypedConverter<RG.Vector3d, SOG.Vector> _vectorConverter;

  public ViewInfoToSpeckleTopLevelConverter(
    ITypedConverter<RG.Point3d, SOG.Point> pointConverter,
    ITypedConverter<RG.Vector3d, SOG.Vector> vectorConverter
  )
  {
    _pointConverter = pointConverter;
    _vectorConverter = vectorConverter;
  }

  public Base Convert(object target) => Convert((ViewInfo)target);

  public SOO.Camera Convert(ViewInfo target)
  {
    if (target.Viewport.IsParallelProjection)
    {
      throw new ConversionException("Parallel projection views are not supported.");
    }

    return new()
    {
      name = target.Name,
      position = _pointConverter.Convert(target.Viewport.CameraLocation),
      up = _vectorConverter.Convert(target.Viewport.CameraY),
      forward = _vectorConverter.Convert(target.Viewport.CameraZ),
    };
  }
}
