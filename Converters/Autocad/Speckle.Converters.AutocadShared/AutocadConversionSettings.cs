namespace Speckle.Converters.Autocad;

public record AutocadConversionSettings
{
  public Document Document { get; init; }
  public string SpeckleUnits { get; init; }
}
