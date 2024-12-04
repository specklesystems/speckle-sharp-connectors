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
public class CSiSendCollectionManager
{
  protected IConverterSettingsStore<CSiConversionSettings> ConverterSettings { get; }
  protected Dictionary<string, Collection> CollectionCache { get; } = new();

  public CSiSendCollectionManager(IConverterSettingsStore<CSiConversionSettings> converterSettings)
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

  protected virtual Collection CreateCollection(Base convertedObject) => new(GetCollectionPath(convertedObject));
}
