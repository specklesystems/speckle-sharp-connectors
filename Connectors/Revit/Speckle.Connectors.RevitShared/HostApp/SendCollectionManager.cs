using Autodesk.Revit.DB;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.Revit.HostApp;

/// <summary>
/// Manages efficiently creating host collections for revit elements on send. Expects to be scoped per send operation.
/// </summary>
public class SendCollectionManager
{
  private readonly ISettingsStore<RevitConversionSettings> _settings;
  private readonly Dictionary<string, Collection> _collectionCache = new();

  public SendCollectionManager(ISettingsStore<RevitConversionSettings> settings)
  {
    _settings = settings;
  }

  /// <summary>
  /// Returns the element's host collection based on level, category and optional type. Additionally, places the host collection on the provided root object.
  /// Note, it's not nice we're mutating the root object in this function.
  /// </summary>
  /// <param name="element"></param>
  /// <param name="rootObject"></param>
  /// <returns></returns>
  public Collection GetAndCreateObjectHostCollection(Element element, Collection rootObject)
  {
    var doc = _settings.Current.Document;
    var path = new List<string>();

    // Step 1: create path components. Currently, this is
    // level > category > type
    path.Add(doc.GetElement(element.LevelId) is not Level level ? "No level" : level.Name);
    path.Add(element.Category?.Name ?? "No category");
    var typeId = element.GetTypeId();
    if (typeId != ElementId.InvalidElementId)
    {
      var type = doc.GetElement(typeId);
      if (type != null)
      {
        path.Add(type.Name);
      }
    }
    else
    {
      path.Add("No type");
    }

    string fullPathName = string.Concat(path);
    if (_collectionCache.TryGetValue(fullPathName, out Collection? value))
    {
      return value;
    }

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
