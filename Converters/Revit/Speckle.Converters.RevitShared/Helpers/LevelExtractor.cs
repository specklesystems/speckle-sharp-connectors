namespace Speckle.Converters.RevitShared.Helpers;

public sealed class LevelExtractor
{
  // stores the map of level id to level name
  private readonly Dictionary<DB.ElementId, DB.Level> _levelCache = new();

  public LevelExtractor() { }

  public string? GetLevelName(DB.Element element)
  {
    var level = GetLevel(element);
    if (level is null)
    {
      return null;
    }

    return level.Name;
  }

  public DB.Level? GetLevel(DB.Element element)
  {
    // get level, if any
    if (element.LevelId != DB.ElementId.InvalidElementId)
    {
      if (_levelCache.TryGetValue(element.LevelId, out DB.Level? cachedLevel))
      {
        return cachedLevel;
      }

      if (element.Document.GetElement(element.LevelId) is DB.Level level)
      {
        _levelCache[element.LevelId] = level;
        return level;
      }
    }

    return null;
  }
}
