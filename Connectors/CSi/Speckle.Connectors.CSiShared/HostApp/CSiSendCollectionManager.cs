using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
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
  private readonly IConverterSettingsStore<CSiConversionSettings> _converterSettings;
  private readonly Dictionary<string, Collection> _collectionCache = new();

  public CSiSendCollectionManager(IConverterSettingsStore<CSiConversionSettings> converterSettings)
  {
    _converterSettings = converterSettings;
  }

  // TODO: Frames could be further classified under Columns, Braces and Beams. Same for Shells which could be classified into walls, floors
  public Collection GetAndCreateObjectHostCollection(ICSiWrapper csiObject, Collection rootObject)
  {
    var path = csiObject.GetType().Name.Replace("Wrapper", ""); // CSiJointWrapper → CSiJoint, CSiFrameWrapper → CSiFrame etc.

    if (_collectionCache.TryGetValue(path, out Collection? value))
    {
      return value;
    }

    Collection childCollection = new(path);
    rootObject.elements.Add(childCollection);
    _collectionCache[path] = childCollection;

    rootObject = childCollection;

    return rootObject;
  }
}
