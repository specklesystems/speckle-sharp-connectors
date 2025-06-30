namespace Speckle.Converters.AutocadShared.ToSpeckle;

public interface IPropertiesExtractor
{
  Dictionary<string, object?> GetProperties(ADB.Entity entity);
}
