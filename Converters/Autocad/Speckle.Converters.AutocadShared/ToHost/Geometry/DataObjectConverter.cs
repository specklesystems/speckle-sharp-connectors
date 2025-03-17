using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Objects.Data;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.AutocadShared.ToHost.Geometry;

[NameAndRankValue(typeof(DataObject), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class DataObjectConverter : IToHostTopLevelConverter, ITypedConverter<DataObject, List<(ADB.Entity a, Base b)>>
{
  private readonly ITypedConverter<ICurve, ADB.Curve> _curveConverter;
  private readonly ITypedConverter<SOG.BrepX, List<(ADB.Entity a, Base b)>> _brepXConverter;
  private readonly ITypedConverter<SOG.ExtrusionX, List<(ADB.Entity a, Base b)>> _extrusionXConverter;
  private readonly ITypedConverter<SOG.Mesh, ADB.PolyFaceMesh> _meshConverter;
  private readonly ITypedConverter<SOG.Point, ADB.DBPoint> _pointConverter;
  private readonly ITypedConverter<SOG.SubDX, List<(ADB.Entity a, Base b)>> _subDXConverter;
  private readonly ITypedConverter<SOG.Region, ADB.Entity> _regionConverter;

  public DataObjectConverter(
    ITypedConverter<ICurve, ADB.Curve> curveConverter,
    ITypedConverter<SOG.BrepX, List<(ADB.Entity a, Base b)>> brepXConverter,
    ITypedConverter<SOG.ExtrusionX, List<(ADB.Entity a, Base b)>> extrusionXConverter,
    ITypedConverter<SOG.Mesh, ADB.PolyFaceMesh> meshConverter,
    ITypedConverter<SOG.Point, ADB.DBPoint> pointConverter,
    ITypedConverter<SOG.SubDX, List<(ADB.Entity a, Base b)>> subDXConverter,
    ITypedConverter<SOG.Region, ADB.Entity> regionConverter
  )
  {
    _curveConverter = curveConverter;
    _brepXConverter = brepXConverter;
    _extrusionXConverter = extrusionXConverter;
    _meshConverter = meshConverter;
    _pointConverter = pointConverter;
    _subDXConverter = subDXConverter;
    _regionConverter = regionConverter;
  }

  public object Convert(Base target) => Convert((DataObject)target);

  public List<(ADB.Entity a, Base b)> Convert(DataObject target)
  {
    var result = new List<(ADB.Entity a, Base b)>();
    foreach (var item in target.displayValue)
    {
      result.AddRange(ConvertDisplayObject(item));
    }
    return result;
  }

  public IEnumerable<(ADB.Entity a, Base b)> ConvertDisplayObject(Base displayObject)
  {
    switch (displayObject)
    {
      case SOG.BrepX brepX:
        foreach (var i in _brepXConverter.Convert(brepX))
        {
          yield return i;
        }
        break;
      case SOG.ExtrusionX extrusionX:
        foreach (var i in _extrusionXConverter.Convert(extrusionX))
        {
          yield return i;
        }
        break;
      case SOG.Mesh mesh:
        yield return (_meshConverter.Convert(mesh), mesh);
        break;
      case SOG.Point point:
        yield return (_pointConverter.Convert(point), point);
        break;
      case ICurve curve:
        yield return (_curveConverter.Convert(curve), (Base)curve);
        break;
      case SOG.SubDX subDX:
        foreach (var i in _subDXConverter.Convert(subDX))
        {
          yield return i;
        }
        break;
      case SOG.Region region:
        yield return (_regionConverter.Convert(region), region);
        break;

      default:
        throw new ConversionException($"Found unsupported geometry: {displayObject.GetType()}");
    }
  }
}
