using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.CSiShared.HostApp;

public class CSiSendCollectionManager
{
  private readonly IConverterSettingsStore<CSiConversionSettings> _converterSettings;
  private readonly Dictionary<string, Collection> _collectionCache = new();

  public CSiSendCollectionManager(IConverterSettingsStore<CSiConversionSettings> converterSettings)
  {
    _converterSettings = converterSettings;
  }

  // TODO: Frames could be further classified under Columns, Braces and Beams. Same for shells: walls, floors
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
