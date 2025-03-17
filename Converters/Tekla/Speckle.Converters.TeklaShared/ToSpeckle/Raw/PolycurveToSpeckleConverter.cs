using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.TeklaShared.ToSpeckle.Raw;

public class PolycurveToSpeckleConverter : ITypedConverter<TG.Polycurve, SOG.Polycurve>
{
  private readonly ITypedConverter<TG.LineSegment, SOG.Line> _lineConverter;
  private readonly ITypedConverter<TG.Arc, SOG.Arc> _arcConverter;
  private readonly IConverterSettingsStore<TeklaConversionSettings> _settingsStore;

  public PolycurveToSpeckleConverter(
    ITypedConverter<TG.LineSegment, SOG.Line> lineConverter,
    ITypedConverter<TG.Arc, SOG.Arc> arcConverter,
    IConverterSettingsStore<TeklaConversionSettings> settingsStore
  )
  {
    _lineConverter = lineConverter;
    _arcConverter = arcConverter;
    _settingsStore = settingsStore;
  }

  public SOG.Polycurve Convert(TG.Polycurve target)
  {
    List<Speckle.Objects.ICurve> segments = new();
    foreach (TG.ICurve curve in target)
    {
      switch (curve)
      {
        case TG.Arc arc:
          segments.Add(_arcConverter.Convert(arc));
          break;

        case TG.LineSegment line:
          segments.Add(_lineConverter.Convert(line));
          break;
      }
    }

    return new()
    {
      segments = segments,
      length = target.Length,
      units = _settingsStore.Current.SpeckleUnits
    };
  }
}
