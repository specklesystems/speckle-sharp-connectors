namespace Speckle.Converters.RevitShared.Helpers;

public sealed class LevelExtractor
{
  // stores the map of level id to level name
  private readonly Dictionary<DB.ElementId, string> _levelCache = new();

  public LevelExtractor() { }

  public string GetLevel(DB.Element element)
  {
    // get level, if any
    if (element.LevelId != DB.ElementId.InvalidElementId)
    {
      if (_levelCache.TryGetValue(element.LevelId, out string? name))
      {
        return name;
      }

      if (element.Document.GetElement(element.LevelId) is DB.Level level)
      {
        _levelCache[element.LevelId] = level.Name;
        return level.Name;
      }
    }

    return "none";
  }
}
