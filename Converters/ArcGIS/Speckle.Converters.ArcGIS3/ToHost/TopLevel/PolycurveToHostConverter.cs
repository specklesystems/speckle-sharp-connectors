using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Sdk.Common;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3.ToHost.TopLevel;

[NameAndRankValue(nameof(SOG.Polycurve), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class PolycurveToHostConverter : IToHostTopLevelConverter, ITypedConverter<SOG.Polycurve, ACG.Polyline>
{
  private readonly IRootToHostConverter _converter;
  private readonly IConverterSettingsStore<ArcGISConversionSettings> _settingsStore;

  public PolycurveToHostConverter(
    IRootToHostConverter converter,
    IConverterSettingsStore<ArcGISConversionSettings> settingsStore
  )
  {
    _converter = converter;
    _settingsStore = settingsStore;
  }

  public HostResult Convert(Base target) => HostResult.Success( Convert((SOG.Polycurve)target));

  public ACG.Polyline Convert(SOG.Polycurve target)
  {
    ACG.MapPoint? lastConvertedPt = null;
    List<ACG.Polyline> segments = new();

    foreach (var segment in target.segments)
    {
      ACG.Polyline converted = (ACG.Polyline)_converter.Convert((Base)segment).Host.NotNull(); 
      List<ACG.MapPoint> segmentPts = converted.Points.ToList();

      if (
        lastConvertedPt != null
        && segmentPts.Count > 0
        && (
          Math.Round(lastConvertedPt.X, 6) != Math.Round(segmentPts[0].X, 6)
          || Math.Round(lastConvertedPt.Y, 6) != Math.Round(segmentPts[0].Y, 6)
          || Math.Round(lastConvertedPt.Z, 6) != Math.Round(segmentPts[0].Z, 6)
        )
      )
      {
        throw new ValidationException("Polycurve segments are not in a correct sequence/orientation");
      }

      lastConvertedPt = segmentPts[^1];
      segments.Add(converted);
    }

    return new ACG.PolylineBuilderEx(
      segments,
      ACG.AttributeFlags.HasZ,
      _settingsStore.Current.ActiveCRSoffsetRotation.SpatialReference
    ).ToGeometry();
  }
}
