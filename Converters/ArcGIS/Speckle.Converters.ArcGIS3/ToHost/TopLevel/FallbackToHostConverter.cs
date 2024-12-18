using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Objects;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3.ToHost.TopLevel;

[NameAndRankValue(nameof(DisplayableObject), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class FallbackToHostConverter : IToHostTopLevelConverter, ITypedConverter<DisplayableObject, ACG.Geometry>
{
  private readonly ITypedConverter<List<SOG.Mesh>, ACG.Multipatch> _meshListConverter;
  private readonly ITypedConverter<List<ICurve>, ACG.Polyline> _icurveListConverter;
  private readonly ITypedConverter<List<SOG.Point>, ACG.Multipoint> _pointListConverter;

  public FallbackToHostConverter(
    ITypedConverter<List<SOG.Mesh>, ACG.Multipatch> meshListConverter,
    ITypedConverter<List<ICurve>, ACG.Polyline> icurveListConverter,
    ITypedConverter<List<SOG.Point>, ACG.Multipoint> pointListConverter
  )
  {
    _meshListConverter = meshListConverter;
    _icurveListConverter = icurveListConverter;
    _pointListConverter = pointListConverter;
  }

  public HostResult Convert(Base target) => HostResult.Success( Convert((DisplayableObject)target));

  public ACG.Geometry Convert(DisplayableObject target)
  {
    if (!target.displayValue.Any())
    {
      throw new ValidationException($"Zero fallback values specified");
    }

    var first = target.displayValue[0];

    return first switch
    {
      ICurve => _icurveListConverter.Convert(target.displayValue.Cast<ICurve>().ToList()),
      SOG.Mesh => _meshListConverter.Convert(target.displayValue.Cast<SOG.Mesh>().ToList()),
      SOG.Point => _pointListConverter.Convert(target.displayValue.Cast<SOG.Point>().ToList()),
      _ => throw new ValidationException($"Found unsupported fallback geometry: {first.GetType()}")
    };
  }
}
