using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.Rhino.ToHost.Raw;

public class TextEntityToHostConverter : ITypedConverter<SA.Text, RG.TextEntity>
{
  private readonly ITypedConverter<SOG.Plane, RG.Plane> _planeConverter;
  private readonly IConverterSettingsStore<RhinoConversionSettings> _settingsStore;

  public TextEntityToHostConverter(
    ITypedConverter<SOG.Plane, RG.Plane> planeConverter,
    IConverterSettingsStore<RhinoConversionSettings> settingsStore
  )
  {
    _planeConverter = planeConverter;
    _settingsStore = settingsStore;
  }

  /// <remarks>⚠️ This conversion DOES perform scaling! this is because <see cref="GeometryBase.Transform"/> does NOT scale text height!</remarks>
  public RG.TextEntity Convert(SA.Text target)
  {
    var scaleFactor = Units.GetConversionFactor(target.units, _settingsStore.Current.SpeckleUnits);

    RG.TextEntity result =
      new()
      {
        Plane = _planeConverter.Convert(target.plane),
        PlainText = target.value,
        TextHeight = target.height * scaleFactor,
        TextOrientation = target.screenOriented ? TextOrientation.InView : TextOrientation.InPlane,
        // text class does not have a scale prop.
        // Scale is built in to the text height on publish, therefore a factor of 1 is always used here.
        DimensionScale = 1.0,
        TextHorizontalAlignment = GetTextHorizontalAlignment(target.alignmentH),
        TextVerticalAlignment = GetTextVerticalAlignment(target.alignmentV),
      };

    // only set the max width if target has a non-null value.
    // otherwise this will result in incorrectly wrapped text
    if (target.maxWidth is double maxWidth)
    {
      result.FormatWidth = maxWidth;
      result.TextIsWrapped = true;
    }

    return result;
  }

  private TextHorizontalAlignment GetTextHorizontalAlignment(SA.AlignmentHorizontal alignment)
  {
    return alignment switch
    {
      SA.AlignmentHorizontal.Left => TextHorizontalAlignment.Left,
      SA.AlignmentHorizontal.Center => TextHorizontalAlignment.Center,
      SA.AlignmentHorizontal.Right => TextHorizontalAlignment.Right,
      _ => throw new ConversionException($"Unexpected horizontal alignment value found on text: {alignment}"),
    };
  }

  private TextVerticalAlignment GetTextVerticalAlignment(SA.AlignmentVertical alignment)
  {
    return alignment switch
    {
      SA.AlignmentVertical.Top => TextVerticalAlignment.Top,
      SA.AlignmentVertical.Center => TextVerticalAlignment.Middle,
      SA.AlignmentVertical.Bottom => TextVerticalAlignment.Bottom,
      _ => throw new ConversionException($"Unexpected vertical alignment value found on text: {alignment}"),
    };
  }
}
