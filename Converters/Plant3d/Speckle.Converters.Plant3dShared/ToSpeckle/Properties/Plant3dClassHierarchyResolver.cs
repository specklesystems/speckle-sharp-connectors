using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.ProcessPower.DataObjects;

namespace Speckle.Converters.Plant3dShared.ToSpeckle;

/// <summary>
/// Resolves the class hierarchy for a given Plant 3D object using the DataLinksManager.
/// The class hierarchy represents the Data Manager category.
/// </summary>
public class Plant3dClassHierarchyResolver
{
  private readonly Dictionary<string, Plant3dClassHierarchy> _classHierarchyCache = [];
  private const string PNP_BASE_CLASS_NAME = "PnPBase";

  /// <summary>
  /// Traverses the object's class hierarchy to provide a fully qualified category and individual category levels.
  /// </summary>
  public Plant3dClassHierarchy Resolve(PPDL.DataLinksManager dataLinksManager, ObjectId objectId)
  {
    ArgumentNullException.ThrowIfNull(dataLinksManager);

    var database = dataLinksManager.GetPnPDatabase();
    var className = dataLinksManager.GetObjectClassname(objectId);

    return Resolve(database, className);
  }

  private Plant3dClassHierarchy Resolve(PnPDatabase database, string className)
  {
    if (string.IsNullOrWhiteSpace(className))
    {
      return new();
    }

    // Class names will be unique as they represent table names in the Plant 3D project database.
    if (_classHierarchyCache.TryGetValue(className, out var cachedHierarchy))
    {
      return cachedHierarchy;
    }

    var hierarchy = BuildHierarchyPath(database, className);
    _classHierarchyCache[className] = hierarchy;

    return hierarchy;
  }

  private static Plant3dClassHierarchy BuildHierarchyPath(PnPDatabase database, string? className)
  {
    List<string> hierarchy = [];

    // Traverse the class hierarchy until we reach the base class (PnPBase) or an empty class name.
    while (
      !string.IsNullOrWhiteSpace(className)
      && !string.Equals(className, PNP_BASE_CLASS_NAME, StringComparison.OrdinalIgnoreCase)
    )
    {
      hierarchy.Add(className);

      PnPTable? classTable = database.Tables.Contains(className) ? database.Tables[className] : null;
      className = classTable?.BaseTableName;
    }

    hierarchy.Reverse();

    return Plant3dClassHierarchy.FromList(hierarchy);
  }
}

/// <summary>
/// Represents a category from the Plant 3D Data Manager, provides the full category, and level by level.
/// </summary>
public record Plant3dClassHierarchy(
  string Category = "",
  string Level1 = "",
  string Level2 = "",
  string Level3 = "",
  string Level4 = "",
  string Level5 = ""
)
{
  private const string CLASS_HIERARCHY_SEPARATOR = " > ";

  public static Plant3dClassHierarchy FromList(IReadOnlyList<string> hierarchy)
  {
    return new(string.Join(CLASS_HIERARCHY_SEPARATOR, hierarchy), Level(0), Level(1), Level(2), Level(3), Level(4));

    string Level(int index)
    {
      return hierarchy.Count > index ? hierarchy[index] : string.Empty;
    }
  }
}
