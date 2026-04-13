using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.GrasshopperShared.Components.Collections;

/// <summary>
/// Shared helper methods for collection components to avoid code duplication
/// </summary>
public static class CollectionHelpers
{
  /// <summary>
  /// Creates a root collection wrapper with default values
  /// </summary>
  public static SpeckleCollectionWrapper CreateRootCollection(string instanceGuid) =>
    new SpeckleCollectionWrapper
    {
      Base = new Collection(),
      Name = "Unnamed",
      Path = new List<string> { "Unnamed" },
      Color = null,
      Material = null,
      ApplicationId = instanceGuid,
    };

  /// <summary>
  /// Validates that all application IDs are unique across the entire collection hierarchy.
  /// </summary>
  /// <returns>True if duplicates exist, false if all IDs are unique</returns>
  public static bool HasDuplicateApplicationIds(SpeckleCollectionWrapper rootCollection)
  {
    var seenIds = new HashSet<string>();
    var duplicateIds = new HashSet<string>();

    ProcessAndCheckForDuplicateApplicationIds(rootCollection, seenIds, duplicateIds);

    return duplicateIds.Count > 0;
  }

  /// <summary>
  /// Recursively collects application IDs from all wrappers in the collection hierarchy.
  /// </summary>
  /// <remarks>
  /// Only checks the wrapper's ApplicationId, not for example geometries within DataObjects.
  /// </remarks>
  private static void ProcessAndCheckForDuplicateApplicationIds(
    SpeckleCollectionWrapper collection,
    HashSet<string> seenIds,
    HashSet<string> duplicateIds
  )
  {
    foreach (var element in collection.Elements)
    {
      switch (element)
      {
        case null:
          break; // skip nulls (CNX-2855)
        case SpeckleCollectionWrapper childCollection:
          // recurse into child collections
          ProcessAndCheckForDuplicateApplicationIds(childCollection, seenIds, duplicateIds);
          break;

        case SpeckleWrapper wrapper:
          if (wrapper.ApplicationId != null && !seenIds.Add(wrapper.ApplicationId))
          {
            duplicateIds.Add(wrapper.ApplicationId);
          }
          break;
      }
    }
  }

  /// <summary>
  /// Recursively checks if collection or any descendants contain valid geometry/data objects
  /// </summary>
  public static bool HasAnyValidContent(ISpeckleCollectionObject? element) =>
    element switch
    {
      SpeckleGeometryWrapper => true,
      SpeckleDataObjectWrapper => true,
      SpeckleCollectionWrapper collection => collection.Elements.Any(HasAnyValidContent),
      _ => false,
    };
}
