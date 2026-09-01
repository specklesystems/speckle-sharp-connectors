using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.CSiShared.HostApp;

/// <summary>
/// We can use the CSiWrappers to create our collection structure.
/// </summary>
/// <remarks>
/// This class manages the collections. If the key (from the path) already exists, this collection is returned.
/// If it doesn't exist, a new collection is created and added to the rootObject.
/// </remarks>
public class CsiSendCollectionManager
{
  protected IConverterSettingsStore<CsiConversionSettings> ConverterSettings { get; }
  protected Dictionary<string, Collection> CollectionCache { get; } = new();

  public CsiSendCollectionManager(IConverterSettingsStore<CsiConversionSettings> converterSettings)
  {
    ConverterSettings = converterSettings;
  }

  public virtual Collection AddObjectCollectionToRoot(Base convertedObject, Collection rootObject)
  {
    var path = GetCollectionPath(convertedObject);

    if (CollectionCache.TryGetValue(path, out Collection? collection))
    {
      return collection;
    }

    Collection childCollection = CreateCollection(convertedObject);
    rootObject.elements.Add(childCollection);
    CollectionCache[path] = childCollection;
    return childCollection;
  }

  protected virtual string GetCollectionPath(Base convertedObject) => convertedObject["type"]?.ToString() ?? "Unknown";

  /// <summary>
  /// The ordered collection segments (outermost → leaf) a converted object groups under. Shared by the v1 send (which
  /// builds a <see cref="Collection"/> tree) and the 4.0 artefact send (which emits nested CONTAINER nodes directly).
  /// Base = flat by type; ETABS overrides to level → category.
  /// </summary>
  public virtual IReadOnlyList<string> GetCollectionSegments(Base convertedObject) =>
    new[] { GetCollectionPath(convertedObject) };

  protected virtual Collection CreateCollection(Base convertedObject) =>
    new() { name = GetCollectionPath(convertedObject) };
}
