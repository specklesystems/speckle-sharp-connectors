using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Rhino7.ToHost.TopLevel;

[NameAndRankValue(nameof(DisplayableObject), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class SpeckleFallbackToAutocadTopLevelConverter
  : IToHostTopLevelConverter,
    ITypedConverter<DisplayableObject, List<ADB.Entity>>
{
  private readonly ITypedConverter<SOG.Line, ADB.Line> _lineConverter;
  private readonly ITypedConverter<SOG.Polyline, ADB.Polyline3d> _polylineConverter;
  private readonly ITypedConverter<SOG.Mesh, ADB.PolyFaceMesh> _meshConverter;
  private readonly ITypedConverter<SOG.Point, ADB.DBPoint> _pointConverter;

  public SpeckleFallbackToAutocadTopLevelConverter(
    ITypedConverter<SOG.Line, ADB.Line> lineConverter,
    ITypedConverter<SOG.Polyline, ADB.Polyline3d> polylineConverter,
    ITypedConverter<SOG.Mesh, ADB.PolyFaceMesh> meshConverter,
    ITypedConverter<SOG.Point, ADB.DBPoint> pointConverter
  )
  {
    _lineConverter = lineConverter;
    _polylineConverter = polylineConverter;
    _meshConverter = meshConverter;
    _pointConverter = pointConverter;
  }

  public object Convert(Base target) => Convert((DisplayableObject)target);

  public List<ADB.Entity> Convert(DisplayableObject target)
  {
    var result = new List<ADB.Entity>();
    foreach (var item in target.displayValue)
    {
      ADB.Entity x = item switch
      {
        SOG.Line line => _lineConverter.Convert(line),
        SOG.Polyline polyline => _polylineConverter.Convert(polyline),
        SOG.Mesh mesh => _meshConverter.Convert(mesh),
        SOG.Point point => _pointConverter.Convert(point),
        _ => throw new NotSupportedException($"Found unsupported fallback geometry: {item.GetType()}")
      };
      result.Add(x);
    }

    return result;
  }
}
