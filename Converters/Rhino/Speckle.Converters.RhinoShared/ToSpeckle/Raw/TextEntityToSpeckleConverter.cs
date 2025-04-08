﻿using Rhino.DocObjects;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Rhino.ToSpeckle.Raw;

public class TextEntityToSpeckleConverter : ITypedConverter<RG.TextEntity, SO.Text>
{
  private readonly ITypedConverter<RG.Point3d, SOG.Point> _pointConverter;
  private readonly ITypedConverter<RG.Plane, SOG.Plane> _planeConverter;
  private readonly IConverterSettingsStore<RhinoConversionSettings> _settingsStore;

  public TextEntityToSpeckleConverter(
    ITypedConverter<RG.Point3d, SOG.Point> pointConverter,
    ITypedConverter<RG.Plane, SOG.Plane> planeConverter,
    IConverterSettingsStore<RhinoConversionSettings> settingsStore
  )
  {
    _pointConverter = pointConverter;
    _planeConverter = planeConverter;
    _settingsStore = settingsStore;
  }

  /// <summary>
  /// Converts a Rhino TextEntity to a Speckle Text object.
  /// </summary>
  /// <param name="target">The Rhino TextEntity to convert.</param>
  /// <returns>The converted Speckle Text object.</returns>
  public SO.Text Convert(RG.TextEntity target) =>
    new()
    {
      value = SplitText(target),
      height = target.TextHeight,
      origin = _pointConverter.Convert(target.Plane.Origin),
      plane = GetTextPlane(target),
      alignmentH = (int)target.TextHorizontalAlignment,
      alignmentV = SimplifyVerticalAlignment((int)target.TextVerticalAlignment),
      units = _settingsStore.Current.SpeckleUnits
    };

  private SOG.Plane? GetTextPlane(RG.TextEntity target)
  {
    // set plane to null if text orientation follows camera view
    if (target.TextOrientation != TextOrientation.InPlane)
    {
      return null;
    }

    if (target.TextRotationRadians == 0)
    {
      return _planeConverter.Convert(target.Plane);
    }
    // adjust text plane if rotation applied
    RG.Plane rotatedPlane =
      new()
      {
        Origin = target.Plane.Origin,
        XAxis = target.Plane.XAxis,
        YAxis = target.Plane.YAxis,
        ZAxis = target.Plane.ZAxis,
      };
    rotatedPlane.Rotate(target.TextRotationRadians, target.Plane.ZAxis);
    return _planeConverter.Convert(rotatedPlane);
  }

  /// <summary>
  /// Split text into more lines if width formatting is applied.
  /// Approximation of Rhino text splitting, not precise: in Rhino, depends on the font and specific characters.
  /// Only useful to keep approximately same amount of text lines and line width
  /// </summary>
  private string SplitText(RG.TextEntity target)
  {
    // return, if formatting doesn't affect the text width
    if (!target.TextIsWrapped)
    {
      return target.PlainText;
    }
    // determine maximum cropped string length (average for all lines)
    string plainText = target.PlainText.EndsWith("\r\n") ? target.PlainText[..^2] : target.PlainText;
    string[] lines = plainText.Split(["\r\n"], StringSplitOptions.None);
    int maxCharCropped = (int)(1.7 * target.TextModelWidth / target.TextHeight);

    // assemble list of cropped strings
    List<string> newLines = new();
    foreach (var line in lines)
    {
      newLines.AddRange(SplitLine(line, maxCharCropped));
    }
    return string.Join("\r\n", newLines);
  }

  private List<string> SplitLine(string line, int maxChars)
  {
    List<string> newLines = new();

    if (line.Length > maxChars)
    {
      newLines.Add(line[..maxChars]);
      newLines.AddRange(SplitLine(line[maxChars..], maxChars));
    }
    else
    {
      newLines.Add(line);
    }

    return newLines;
  }

  /// <summary>
  /// Simplify alignment to just 3 options: Top (1-2), Middle (3), Bottom (4-6)
  /// </summary>
  private int SimplifyVerticalAlignment(int alignment) => alignment < 3 ? 0 : (alignment == 3 ? 1 : 2);
}
