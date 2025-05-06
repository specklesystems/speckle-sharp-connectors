using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class TextNoteToSpeckleConverter : ITypedConverter<DB.TextNote, SA.Text>
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly ITypedConverter<DB.XYZ, SOG.Vector> _vectorConverter;
  private readonly ITypedConverter<DB.XYZ, SOG.Point> _xyzConverter;

  public TextNoteToSpeckleConverter(
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    ITypedConverter<DB.XYZ, SOG.Vector> vectorConverter,
    ITypedConverter<DB.XYZ, SOG.Point> xyzConverter
  )
  {
    _converterSettings = converterSettings;
    _vectorConverter = vectorConverter;
    _xyzConverter = xyzConverter;
  }

  public SA.Text Convert(DB.TextNote target)
  {
    SOG.Point origin = _xyzConverter.Convert(target.Coord);
    return new()
    {
      value = target.Text,
      height = target.Height,
      maxWidth = target.Width,
      origin = origin,
      plane = new SOG.Plane
      {
        origin = origin,
        normal = new SOG.Vector()
        {
          x = 0,
          y = 0,
          z = 1,
          units = _converterSettings.Current.SpeckleUnits
        },
        xdir = _vectorConverter.Convert(target.BaseDirection),
        ydir = _vectorConverter.Convert(target.UpDirection),
        units = _converterSettings.Current.SpeckleUnits
      },
      alignmentH = GetHorizontalAlignment(target.HorizontalAlignment),
      alignmentV = GetVerticalAlignment(target.VerticalAlignment),
      units = _converterSettings.Current.SpeckleUnits
    };
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
