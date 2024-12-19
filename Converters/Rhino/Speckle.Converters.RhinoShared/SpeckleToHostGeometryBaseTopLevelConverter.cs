using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Rhino;

public abstract class SpeckleToHostGeometryBaseTopLevelConverter<TIn, TOut> : IToHostTopLevelConverter
  where TIn : Base
{
  private readonly IConverterSettingsStore<RhinoConversionSettings> _settingsStore;
  private readonly ITypedConverter<TIn, TOut> _geometryBaseConverter;

  protected SpeckleToHostGeometryBaseTopLevelConverter(
    IConverterSettingsStore<RhinoConversionSettings> settingsStore,
    ITypedConverter<TIn, TOut> geometryBaseConverter
  )
  {
    _settingsStore = settingsStore;
    _geometryBaseConverter = geometryBaseConverter;
  }

  public HostResult Convert(Base target)
  {
    var castedBase = (TIn)target;
    var result = _geometryBaseConverter.Convert(castedBase);

    if (result is null)
    {
      throw new ConversionException(
        $"Geometry base converter returned null for base object of type {target.speckle_type}"
      );
    }

    var units = castedBase["units"] as string;
    if (result is RG.GeometryBase geometryBase && units is not null)
    {
      geometryBase.Transform(GetScaleTransform(units));
      return HostResult.Success(geometryBase);
    }

    if (result is List<RG.GeometryBase> geometryBases && units is not null)
    {
      var t = GetScaleTransform(units);
      foreach (var gb in geometryBases)
      {
        gb.Transform(t);
      }

      return HostResult.Success(geometryBases);
    }

    return HostResult.Success(result);
  }

  private RG.Transform GetScaleTransform(string from)
  {
    var scaleFactor = Units.GetConversionFactor(from, _settingsStore.Current.SpeckleUnits);
    var scale = RG.Transform.Scale(RG.Point3d.Origin, scaleFactor);
    return scale;
  }
}
