namespace Speckle.Converters.RevitShared.Settings;

public record RevitConversionSettings
{
  public DB.Document Document { get; init; }
  public DetailLevelType DetailLevel { get; init; }
  public DB.Transform? ReferencePointTransform { get; init; }
  public double Tolerance { get; init; } = 0.0164042; // 5mm in ft

  public string SpeckleUnits { get; init; }
}
