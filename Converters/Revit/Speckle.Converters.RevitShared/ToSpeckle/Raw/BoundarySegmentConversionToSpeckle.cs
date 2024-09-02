using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Objects;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class BoundarySegmentConversionToSpeckle : ITypedConverter<IList<DB.BoundarySegment>, SOG.Polycurve>
{
  private readonly ITypedConverter<DB.Curve, ICurve> _curveConverter;
  private readonly ISettingsStore<RevitConversionSettings> _settings;

  public BoundarySegmentConversionToSpeckle(
    ITypedConverter<DB.Curve, ICurve> curveConverter,
    ISettingsStore<RevitConversionSettings> settings
  )
  {
    _curveConverter = curveConverter;
    _settings = settings;
  }

  public SOG.Polycurve Convert(IList<DB.BoundarySegment> target)
  {
    if (target.Count == 0)
    {
      throw new ArgumentException("Input Boundary segment list must at least have 1 segment");
    }

    List<ICurve> segments = new(target.Count);
    foreach (var segment in target)
    {
      DB.Curve revitCurve = segment.GetCurve();
      var curve = _curveConverter.Convert(revitCurve);

      // POC: We used to attach the `elementID` of every curve in a PolyCurve as a dynamic property.
      // We've removed this as it seemed unnecessary.

      segments.Add(curve);
    }

    var poly = new SOG.Polycurve { segments = segments, units = _settings.Current.SpeckleUnits };
    return poly;
  }
}
