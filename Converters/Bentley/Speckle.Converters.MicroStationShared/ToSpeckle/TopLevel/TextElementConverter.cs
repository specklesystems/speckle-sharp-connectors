using Speckle.Converter.MicroStation.Extensions;
using Speckle.Converter.MicroStation.Settings;
using Speckle.Converters.Common;
using Speckle.Objects.Annotation;
using Speckle.Objects.Geometry;
using Speckle.Sdk.Models;

namespace Speckle.Converter.MicroStation.ToSpeckle.TopLevel;

/// <summary>
/// Converts a MicroStation 2026 COM <see cref="MSIDGN.TextElement"/> to a Speckle <see cref="Text"/>.
/// The COM API exposes <c>Origin</c> in master units and <c>Text</c> as the display string.
/// <c>TextElement.Origin</c> is a COM ref-setter property so it must be accessed via <c>get_Origin()</c>.
/// </summary>
[NameAndRankValue(typeof(MSIDGN.TextElement), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class TextElementConverter(IConverterSettingsStore<MicroStationConversionSettings> settingsStore)
  : IToSpeckleTopLevelConverter
{
  public Base Convert(object target) => Convert((MSIDGN.TextElement)target);

  private Text Convert(MSIDGN.TextElement element)
  {
    var s = settingsStore.Current;
    var origin = element.get_Origin().ToSpecklePoint(s.SpeckleUnits);

    return new Text
    {
      plane = new Plane
      {
        origin = origin,
        normal = new Vector(0, 0, 1, s.SpeckleUnits),
        xdir = new Vector(1, 0, 0, s.SpeckleUnits),
        ydir = new Vector(0, 1, 0, s.SpeckleUnits),
        units = s.SpeckleUnits,
      },
      value = element.Text ?? string.Empty,
      height = element.TextStyle?.Height ?? 1.0,
      screenOriented = false,
      alignmentH = AlignmentHorizontal.Left,
      alignmentV = AlignmentVertical.Bottom,
      units = s.SpeckleUnits,
      applicationId = element.ID.ToString(),
    };
  }
}
