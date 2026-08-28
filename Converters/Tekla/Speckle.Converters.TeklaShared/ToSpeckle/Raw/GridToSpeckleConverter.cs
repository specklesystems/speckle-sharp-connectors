using System.Globalization;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;

namespace Speckle.Converters.TeklaShared.ToSpeckle.Raw;

public class GridToSpeckleConverter : ITypedConverter<TSM.Grid, IEnumerable<Base>>
{
  private readonly IConverterSettingsStore<TeklaConversionSettings> _settingsStore;

  public GridToSpeckleConverter(IConverterSettingsStore<TeklaConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  // NOTE: from the axis length, not its X component. The component shrinks as the grid rotates, which silently
  // scaled grids and divided by zero at 90 degrees.
  private static double GetScaleFactor(TG.CoordinateSystem coordinateSystem) =>
    coordinateSystem.AxisX.GetLength() / 1000.0;

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

    // the coordinates are local to the grid, so we place them through its coordinate system to land in the same
    // frame as every other object we send
    var xAxis = coordinateSystem.AxisX.GetNormal();
    var yAxis = coordinateSystem.AxisY.GetNormal();

    foreach (var x in xCoordinates)
    {
      yield return new SOG.Line
      {
        start = ToSpecklePoint(coordinateSystem, xAxis, yAxis, x, extendedMinY, conversionFactor),
        end = ToSpecklePoint(coordinateSystem, xAxis, yAxis, x, extendedMaxY, conversionFactor),
        units = _settingsStore.Current.SpeckleUnits,
      };
    }

    foreach (var y in yCoordinates)
    {
      yield return new SOG.Line
      {
        start = ToSpecklePoint(coordinateSystem, xAxis, yAxis, extendedMinX, y, conversionFactor),
        end = ToSpecklePoint(coordinateSystem, xAxis, yAxis, extendedMaxX, y, conversionFactor),
        units = _settingsStore.Current.SpeckleUnits,
      };
    }
  }

  // we build the points directly rather than through the point converter, as the conversion factor is applied here
  private SOG.Point ToSpecklePoint(
    TG.CoordinateSystem coordinateSystem,
    TG.Vector xAxis,
    TG.Vector yAxis,
    double localX,
    double localY,
    double conversionFactor
  )
  {
    var origin = coordinateSystem.Origin;

    return new SOG.Point(
      (origin.X + (localX * xAxis.X) + (localY * yAxis.X)) * conversionFactor,
      (origin.Y + (localX * xAxis.Y) + (localY * yAxis.Y)) * conversionFactor,
      (origin.Z + (localX * xAxis.Z) + (localY * yAxis.Z)) * conversionFactor,
      _settingsStore.Current.SpeckleUnits
    );
  }
}
