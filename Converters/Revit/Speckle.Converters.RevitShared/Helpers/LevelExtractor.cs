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
    // category-specific level parameter (stairs, ramps, roofs)
    else if (GetCategoryLevelParam(category) is { } categoryParam)
    {
      levelId = element.get_Parameter(categoryParam)?.AsElementId();
    }
    // railings: recurse into host (e.g. a stair or floor)
    else if (
      category is DB.BuiltInCategory.OST_StairsRailing
      && element is DB.Architecture.Railing railing
      && railing.HostId != DB.ElementId.InvalidElementId
      && element.Document?.GetElement(railing.HostId) is { } host
    )
    {
      return GetLevel(host);
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

  private static DB.BuiltInParameter? GetCategoryLevelParam(DB.BuiltInCategory? category) =>
    category switch
    {
      DB.BuiltInCategory.OST_Stairs or DB.BuiltInCategory.OST_Ramps => DB.BuiltInParameter.STAIRS_BASE_LEVEL_PARAM,
      DB.BuiltInCategory.OST_Roofs => DB.BuiltInParameter.ROOF_CONSTRAINT_LEVEL_PARAM,
      DB.BuiltInCategory.OST_StructuralFraming => DB.BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM,
      _ => null,
    };

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
