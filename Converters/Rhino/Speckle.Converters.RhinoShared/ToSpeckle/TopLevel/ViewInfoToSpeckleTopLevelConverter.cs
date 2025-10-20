using Rhino.DocObjects;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Rhino.ToSpeckle.TopLevel;

[NameAndRankValue(typeof(ViewInfo), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class ViewInfoToSpeckleTopLevelConverter : IToSpeckleTopLevelConverter, ITypedConverter<ViewInfo, SOO.View>
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

  public SOO.View Convert(ViewInfo target)
  {
    SOG.Point origin = _pointConverter.Convert(target.Viewport.CameraLocation);
    SOG.Vector up = _vectorConverter.Convert(target.Viewport.CameraUp);

    return new() { };
  }
}
