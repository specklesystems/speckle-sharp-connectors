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

  /// <summary>
  /// Gets the level associated with an element. Handles face-based family instances and hosted elements.
  /// </summary>
  public DB.Level? GetLevel(DB.Element element)
  {
    DB.ElementId? levelId = null;

    // hosted/categoryless elements (views, sketch planes, element types) have a null Category;
    // pattern-matching against this local is null-safe, so all category checks below tolerate that.
    var category = element.Category?.BuiltInCategory;

    // try direct LevelId first
    if (element.LevelId != DB.ElementId.InvalidElementId)
    {
      levelId = element.LevelId;
    }
    // stairs store their base level in a parameter
    else if (category is DB.BuiltInCategory.OST_Stairs or DB.BuiltInCategory.OST_Ramps)
    {
      levelId = element.get_Parameter(DB.BuiltInParameter.STAIRS_BASE_LEVEL_PARAM)?.AsElementId();
    }
    // roofs store their base constraint level in a parameter
    else if (category is DB.BuiltInCategory.OST_Roofs)
    {
      levelId = element.get_Parameter(DB.BuiltInParameter.ROOF_CONSTRAINT_LEVEL_PARAM)?.AsElementId();
    }
    // railings: recurse into host (e.g. a stair or floor)
    else if (category is DB.BuiltInCategory.OST_StairsRailing && element is DB.Architecture.Railing railing)
    {
      var hostId = railing.HostId;
      if (hostId != DB.ElementId.InvalidElementId)
      {
        var host = element.Document?.GetElement(hostId);
        if (host != null)
        {
          return GetLevel(host);
        }
      }
    }
    // otherwise try FamilyInstance-specific sources
    else if (element is DB.FamilyInstance familyInstance)
    {
      levelId = TryGetFamilyInstanceLevelId(familyInstance);

      // couldn't find a direct level ID - recurse to host
      if (levelId == null && familyInstance.Host != null)
      {
        return GetLevel(familyInstance.Host);
      }
    }

    // fall back to the sketch plane parameter (sketched elements)
    if (levelId == null || levelId == DB.ElementId.InvalidElementId)
    {
      levelId = element.get_Parameter(DB.BuiltInParameter.SKETCH_PLANE_PARAM)?.AsElementId();
    }

    // okay, no valid LevelId found and we've tried A LOT!
    if (levelId == null || levelId == DB.ElementId.InvalidElementId)
    {
      return null;
    }

    // check if cache has seen this Level before
    if (_levelCache.TryGetValue(levelId, out DB.Level? cached))
    {
      return cached;
    }

    // add to the cache if firs occurence of this level
    if (element.Document?.GetElement(levelId) is DB.Level level)
    {
      _levelCache[levelId] = level;
      return level;
    }

    return null;
  }

  /// <summary>
  /// Tries to get a level ID from a FamilyInstance via parameter or host.
  /// Face-based instances store their level in INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM.
  /// </summary>
  /// <remarks>
  /// See: https://forums.autodesk.com/t5/revit-api-forum/newfamilyinstance-not-setting-level-of-family-instance/td-p/11405934
  /// </remarks>
  private DB.ElementId? TryGetFamilyInstanceLevelId(DB.FamilyInstance familyInstance)
  {
    // try parameter-based level first (face-based families)
    var levelId = familyInstance.get_Parameter(DB.BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM)?.AsElementId();
    if (levelId != null && levelId != DB.ElementId.InvalidElementId)
    {
      return levelId;
    }

    // try host if it's directly a level
    if (familyInstance.Host is DB.Level hostLevel)
    {
      return hostLevel.Id;
    }

    return null;
  }
}
