using Speckle.Converters.Common;

namespace Speckle.Converters.Autocad;

public class AutocadConversionSettings : IConverterSettings
{
  public Document Document { get; init; }
  public string SpeckleUnits { get; init; }
}
