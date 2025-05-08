using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class TextNoteToSpeckleConverter : ITypedConverter<DB.TextNote, SA.Text>
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly IScalingServiceToSpeckle _toSpeckleScalingService;
  private readonly ITypedConverter<DB.XYZ, SOG.Vector> _vectorConverter;
  private readonly ITypedConverter<DB.XYZ, SOG.Point> _xyzConverter;

  public TextNoteToSpeckleConverter(
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    IScalingServiceToSpeckle toSpeckleScalingService,
    ITypedConverter<DB.XYZ, SOG.Vector> vectorConverter,
    ITypedConverter<DB.XYZ, SOG.Point> xyzConverter
  )
  {
    _converterSettings = converterSettings;
    _toSpeckleScalingService = toSpeckleScalingService;
    _vectorConverter = vectorConverter;
    _xyzConverter = xyzConverter;
  }

  public SA.Text Convert(DB.TextNote target)
  {
    SOG.Point origin = _xyzConverter.Convert(target.Coord);

    // for now, in the absence of decision on how to best treat the text sent from 2d views (plans or sections),
    // we always place the text in the horizontal XY plane without custom rotation.
    return new()
    {
      value = target.Text,
      origin = origin,
      height = GetTextHeight(target),
      maxWidth = null,
      plane = null,
      alignmentH = GetHorizontalAlignment(target.HorizontalAlignment),
      alignmentV = GetVerticalAlignment(target.VerticalAlignment),
      units = _converterSettings.Current.SpeckleUnits
    };
  }

  private double GetTextHeight(DB.TextNote textNote)
  {
    DB.BuiltInParameter paraIndex = DB.BuiltInParameter.TEXT_SIZE;
    DB.Parameter textSizeParameter = textNote.Symbol.get_Parameter(paraIndex);
    return _toSpeckleScalingService.Scale(textSizeParameter.AsDouble(), textSizeParameter.GetUnitTypeId());
  }

  /// <summary>
  /// Simplify horizontal text alignment to 3 options: Left, Center, Right
  /// </summary>
  private SA.AlignmentHorizontal GetHorizontalAlignment(DB.HorizontalTextAlignment alignment)
  {
    return alignment switch
    {
      DB.HorizontalTextAlignment.Left => SA.AlignmentHorizontal.Left,
      DB.HorizontalTextAlignment.Center => SA.AlignmentHorizontal.Center,
      DB.HorizontalTextAlignment.Right => SA.AlignmentHorizontal.Right,
      _ => SA.AlignmentHorizontal.Left,
    };
  }

  /// <summary>
  /// Simplify vertical text alignment to 3 options: Top, Center, Bottom
  /// </summary>
  private SA.AlignmentVertical GetVerticalAlignment(DB.VerticalTextAlignment alignment)
  {
    return alignment switch
    {
      DB.VerticalTextAlignment.Top => SA.AlignmentVertical.Top,
      DB.VerticalTextAlignment.Middle => SA.AlignmentVertical.Center,
      DB.VerticalTextAlignment.Bottom => SA.AlignmentVertical.Bottom,
      _ => SA.AlignmentVertical.Top,
    };
  }
}
