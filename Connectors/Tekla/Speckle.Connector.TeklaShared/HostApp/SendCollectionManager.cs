using Speckle.Converters.Common;
using Speckle.Converters.TeklaShared;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.TeklaShared.HostApp;

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
    // Very high-level, would be good to have sub-groups in future releases
    // TODO: Refine further according to section types (for beams), constituent elements (for components) etc. at later stage
    var path = teklaObject.GetType().ToString().Split('.').Last();

    // NOTE: First pass at seeing if a collection key already exists
    if (_collectionCache.TryGetValue(path, out Collection? value))
    {
      return value;
    }

    // NOTE: As this point, we need to create a suitable collection
    // This would be done using a recursive approach to see where to add collection
    // However, since data structure is flat, this returns quick (Ref: Revit ;) )
    Collection childCollection = new(path);
    rootObject.elements.Add(childCollection);
    _collectionCache[path] = childCollection;

    rootObject = childCollection;

    return rootObject;
  }
}
