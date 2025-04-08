using System.Globalization;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;

namespace Speckle.Converters.TeklaShared.ToSpeckle.Raw;

public class GridToSpeckleConverter : ITypedConverter<TSM.Grid, IEnumerable<Base>>
{
  private readonly IConverterSettingsStore<TeklaConversionSettings> _settingsStore;
  private readonly ITypedConverter<TG.LineSegment, SOG.Line> _lineConverter;

  public GridToSpeckleConverter(
    IConverterSettingsStore<TeklaConversionSettings> settingsStore,
    ITypedConverter<TG.LineSegment, SOG.Line> lineConverter
  )
  {
    _settingsStore = settingsStore;
    _lineConverter = lineConverter;
  }

  // this function gets the scale factor from the coordinate system
  // helps us to avoid conflicts between "," and "."
  private double GetScaleFactor(TG.CoordinateSystem coordinateSystem)
  {
    return coordinateSystem.AxisX.X / 1000.0;
  }

  private IEnumerable<double> ParseCoordinateString(string coordinateString)
  {
    if (string.IsNullOrEmpty(coordinateString))
    {
      yield break;
    }

    var numberStyles = NumberStyles.Float;
    var culture = CultureInfo.InvariantCulture;

    var parts = coordinateString.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
    double lastValue = 0;

    foreach (var part in parts)
    {
      if (part.Contains("*"))
      {
        var repetitionParts = part.Split(new[] { '*' }, StringSplitOptions.RemoveEmptyEntries);
        if (
          repetitionParts.Length == 2
          && int.TryParse(repetitionParts[0], numberStyles, culture, out int count)
          && double.TryParse(repetitionParts[1], numberStyles, culture, out double increment)
        )
        {
          double baseValue = lastValue;
          for (int i = 1; i <= count; i++)
          {
            double value = baseValue + (increment * i);
            yield return value;
            lastValue = value;
          }
        }
      }
      else
      {
        if (double.TryParse(part, numberStyles, culture, out double value))
        {
          yield return value;
          lastValue = value;
        }
      }
    }
  }

  public IEnumerable<Base> Convert(TSM.Grid target)
  {
    var coordinateSystem = target.GetCoordinateSystem();
    if (coordinateSystem == null)
    {
      yield break;
    }

    double conversionFactor = Units.GetConversionFactor(Units.Millimeters, _settingsStore.Current.SpeckleUnits);
    var scale = GetScaleFactor(coordinateSystem);

    var xCoordinates = ParseCoordinateString(target.CoordinateX).Select(x => (x / scale) * conversionFactor).ToList();
    var yCoordinates = ParseCoordinateString(target.CoordinateY).Select(y => (y / scale) * conversionFactor).ToList();

    double minX = xCoordinates.Min();
    double maxX = xCoordinates.Max();
    double minY = yCoordinates.Min();
    double maxY = yCoordinates.Max();

    double extendedMinX = minX - ((target.ExtensionLeftX / scale) * conversionFactor);
    double extendedMaxX = maxX + ((target.ExtensionRightX / scale) * conversionFactor);
    double extendedMinY = minY - ((target.ExtensionLeftY / scale) * conversionFactor);
    double extendedMaxY = maxY + ((target.ExtensionRightY / scale) * conversionFactor);

    double scaledZ = (coordinateSystem.Origin.Z / scale) * conversionFactor;

    foreach (var x in xCoordinates)
    {
      var startPoint = new TG.Point(x, extendedMinY, scaledZ);
      var endPoint = new TG.Point(x, extendedMaxY, scaledZ);

      // we're using the Point converter indirectly through the Line converter
      // since we've already applied the conversion factor to the coordinates,
      // we need to tell the Point converter not to apply it again
      var line = new SOG.Line
      {
        start = new SOG.Point(startPoint.X, startPoint.Y, startPoint.Z, _settingsStore.Current.SpeckleUnits),
        end = new SOG.Point(endPoint.X, endPoint.Y, endPoint.Z, _settingsStore.Current.SpeckleUnits),
        units = _settingsStore.Current.SpeckleUnits
      };

      yield return line;
    }

    foreach (var y in yCoordinates)
    {
      var startPoint = new TG.Point(extendedMinX, y, scaledZ);
      var endPoint = new TG.Point(extendedMaxX, y, scaledZ);

      var line = new SOG.Line
      {
        start = new SOG.Point(startPoint.X, startPoint.Y, startPoint.Z, _settingsStore.Current.SpeckleUnits),
        end = new SOG.Point(endPoint.X, endPoint.Y, endPoint.Z, _settingsStore.Current.SpeckleUnits),
        units = _settingsStore.Current.SpeckleUnits
      };

      yield return line;
    }
  }
}
