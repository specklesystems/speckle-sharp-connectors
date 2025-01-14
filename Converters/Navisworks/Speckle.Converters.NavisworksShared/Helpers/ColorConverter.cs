using Convert = System.Convert;
using Sys = System.Drawing;

namespace Speckle.Converter.Navisworks.Helpers;

public static class ColorConverter
{
  /// <summary>
  /// Converts a Navisworks color to a System.Drawing.Color.
  /// </summary>
  /// <param name="color">The Navisworks color to convert</param>
  /// <param name="transparency">Optional transparency value (0.0 to 1.0)</param>
  /// <returns>A System.Drawing.Color with the converted values</returns>
  public static Sys.Color NavisworksColorToColor(NAV.Color color, double? transparency = null)
  {
    var alpha = transparency.HasValue ? Convert.ToInt32((1.0 - transparency.Value) * 255) : 255;

    return Sys.Color.FromArgb(
      alpha: alpha,
      red: Convert.ToInt32(color.R * 255),
      green: Convert.ToInt32(color.G * 255),
      blue: Convert.ToInt32(color.B * 255)
    );
  }

  /// <summary>
  /// Converts RGB values to a color hash string.
  /// </summary>
  /// <param name="color">The Navisworks color</param>
  /// <param name="transparency">Optional transparency value</param>
  /// <returns>A unique hash string for the color</returns>
  public static string GetColorHash(NAV.Color color, double? transparency = null)
  {
    var rgbValues =
      Convert.ToInt32(color.R * 255) << 16 | Convert.ToInt32(color.G * 255) << 8 | Convert.ToInt32(color.B * 255);

    if (!transparency.HasValue)
    {
      return rgbValues.ToString();
    }

    var alpha = Convert.ToInt32((1.0 - transparency.Value) * 255);
    return $"{rgbValues}_{alpha}";
  }

  /// <summary>
  /// Gets a human-readable color name.
  /// </summary>
  /// <param name="color">The Navisworks color to name</param>
  /// <returns>A descriptive name for the color</returns>
  public static string GetColorName(NAV.Color color)
  {
    var converted = NavisworksColorToColor(color);
    return converted.IsKnownColor ? converted.Name : $"Custom_{converted.ToArgb():X8}";
  }
}
