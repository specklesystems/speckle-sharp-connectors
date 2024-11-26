namespace Speckle.Converter.Navisworks.Settings;

public record NavisworksConversionSettings
{
  public NavisworksConversionSettings(NAV.Document document, string speckleUnits)
  {
    this.Document = document;
    this.SpeckleUnits = speckleUnits;
  }

  public NAV.Document Document { get; }
  public string SpeckleUnits { get; }
}
