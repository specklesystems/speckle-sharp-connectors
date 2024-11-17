using Speckle.Converter.Tekla2024;
using Speckle.Converters.Common;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connector.Tekla2024.HostApp;

public class SendCollectionManager
{
  private readonly IConverterSettingsStore<TeklaConversionSettings> _converterSettings;
  private readonly Dictionary<string, Collection> _collectionCache = new();

  public SendCollectionManager(IConverterSettingsStore<TeklaConversionSettings> converterSettings)
  {
    _converterSettings = converterSettings;
  }

  public Collection GetAndCreateObjectHostCollection(TSM.ModelObject teklaObject, Collection rootObject)
  {
    // Tekla Data Structure: rootObject > objectType > name
    // Very high-level at this stage. Would be good to have sub-groups in future releases
    // TODO: Refine further according to section types (for beams), constituent elements (for components) etc. at later stage
    var path = new List<string>();
    path.Add(teklaObject.GetType().ToString().Split('.').Last());

    // NOTE: First pass at seeing if a collection key already exists
    string fullPathName = string.Concat(path);
    if (_collectionCache.TryGetValue(fullPathName, out Collection? value))
    {
      return value;
    }

    // NOTE: As this point, we need to create a suitable collection
    // This would be using a recursive approach to see where to add collection
    // However, since data structure is flat, this returns quick (shoutout to Revit at this stage ;) )
    string flatPathName = "";
    Collection previousCollection = rootObject;

    foreach (var pathItem in path)
    {
      flatPathName += pathItem;
      Collection childCollection;
      if (_collectionCache.TryGetValue(flatPathName, out Collection? collection))
      {
        childCollection = collection;
      }
      else
      {
        childCollection = new Collection(pathItem);
        previousCollection.elements.Add(childCollection);
        _collectionCache[flatPathName] = childCollection;
      }

      previousCollection = childCollection;
    }

    return previousCollection;
  }
}
