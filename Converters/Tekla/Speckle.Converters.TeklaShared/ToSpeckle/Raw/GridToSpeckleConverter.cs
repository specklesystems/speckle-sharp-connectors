using System.Globalization;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converter.Tekla2024.ToSpeckle.Raw;

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

  // this function is to check system global seperator
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

    var scale = GetScaleFactor(coordinateSystem);

    var xCoordinates = ParseCoordinateString(target.CoordinateX).Select(x => x / scale).ToList();
    var yCoordinates = ParseCoordinateString(target.CoordinateY).Select(y => y / scale).ToList();

    double minX = xCoordinates.Min();
    double maxX = xCoordinates.Max();
    double minY = yCoordinates.Min();
    double maxY = yCoordinates.Max();

    double extendedMinX = minX - (target.ExtensionLeftX / scale);
    double extendedMaxX = maxX + (target.ExtensionRightX / scale);
    double extendedMinY = minY - (target.ExtensionLeftY / scale);
    double extendedMaxY = maxY + (target.ExtensionRightY / scale);

    double scaledZ = coordinateSystem.Origin.Z / scale;

    foreach (var x in xCoordinates)
    {
      var startPoint = new TG.Point(x, extendedMinY, scaledZ);
      var endPoint = new TG.Point(x, extendedMaxY, scaledZ);
      yield return _lineConverter.Convert(new TG.LineSegment(startPoint, endPoint));
    }

    foreach (var y in yCoordinates)
    {
      var startPoint = new TG.Point(extendedMinX, y, scaledZ);
      var endPoint = new TG.Point(extendedMaxX, y, scaledZ);
      yield return _lineConverter.Convert(new TG.LineSegment(startPoint, endPoint));
    }
  }
}
