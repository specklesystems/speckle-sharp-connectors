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

  private IEnumerable<double> ParseCoordinateString(string coordinateString)
  {
    if (string.IsNullOrEmpty(coordinateString))
    {
      yield break;
    }

    var parts = coordinateString.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
    double lastValue = 0;

    foreach (var part in parts)
    {
      if (part.Contains("*"))
      {
        var repetitionParts = part.Split(new[] { '*' }, StringSplitOptions.RemoveEmptyEntries);
        if (
          repetitionParts.Length == 2
          && int.TryParse(repetitionParts[0], out int count)
          && double.TryParse(repetitionParts[1], out double increment)
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
        if (double.TryParse(part, out double value))
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

    var xCoordinates = ParseCoordinateString(target.CoordinateX).ToList();
    var yCoordinates = ParseCoordinateString(target.CoordinateY).ToList();

    double minX = xCoordinates.Min();
    double maxX = xCoordinates.Max();
    double minY = yCoordinates.Min();
    double maxY = yCoordinates.Max();

    double extendedMinX = minX - target.ExtensionLeftX;
    double extendedMaxX = maxX + target.ExtensionRightX;
    double extendedMinY = minY - target.ExtensionLeftY;
    double extendedMaxY = maxY + target.ExtensionRightY;

    foreach (var x in xCoordinates)
    {
      var startPoint = new TG.Point(x, extendedMinY, coordinateSystem.Origin.Z);
      var endPoint = new TG.Point(x, extendedMaxY, coordinateSystem.Origin.Z);
      yield return _lineConverter.Convert(new TG.LineSegment(startPoint, endPoint));
    }

    foreach (var y in yCoordinates)
    {
      var startPoint = new TG.Point(extendedMinX, y, coordinateSystem.Origin.Z);
      var endPoint = new TG.Point(extendedMaxX, y, coordinateSystem.Origin.Z);
      yield return _lineConverter.Convert(new TG.LineSegment(startPoint, endPoint));
    }
  }
}
