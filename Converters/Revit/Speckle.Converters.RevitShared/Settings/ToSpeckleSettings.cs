namespace Speckle.Converters.RevitShared.Settings;

public enum DetailLevelType
{
  Coarse,
  Medium,
  Fine
}

public record ToSpeckleSettings(DetailLevelType DetailLevel);
