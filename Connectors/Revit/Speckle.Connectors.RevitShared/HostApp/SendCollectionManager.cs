using System.IO;
using Autodesk.Revit.DB;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Sdk;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.Revit.HostApp;

/// <summary>
/// Manages efficiently creating host collections for revit elements on send. Expects to be scoped per send operation.
/// </summary>
public class SendCollectionManager
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly Dictionary<string, Collection> _collectionCache = new();
  private readonly Dictionary<ElementId, string> _levelCache = new(); // stores level id and its properties
  private readonly Dictionary<string, Collection> _linkedModelCollections = new(); // cache for linked model collections
  private Collection? _mainModelCollection; // collection for main model elements

  public SendCollectionManager(IConverterSettingsStore<RevitConversionSettings> converterSettings)
  {
    _converterSettings = converterSettings;
  }

  /// <summary>
  /// Returns the element's host collection based on level, category and optional type if the main model only is sent.
  /// The host collection is placed on the provided root object.
  /// If linked models are being sent, nested collections are sent under the provided root object.
  /// Note, it's not nice we're mutating the root object in this function.
  /// </summary>
  /// <param name="element"></param>
  /// <param name="rootObject"></param>
  /// <returns></returns>
  public Collection GetAndCreateObjectHostCollection(
    Element element,
    Collection rootObject,
    bool sendWithLinkedModels,
    string? modelDisplayName = null
  )
  {
    var doc = _converterSettings.Current.Document;
    var path = new List<string>();

    // Get model path and name
    string modelPath = doc.PathName;
    string modelName = Path.GetFileNameWithoutExtension(modelPath);
    bool isLinkedModel = doc.IsLinked;

    // Set up the correct hierarchy based on whether we have linked models or not
    Collection startingCollection;

    if (sendWithLinkedModels) // this arg comes from RevitRootObjectBuilder and check is setting enabled and linked models present
    {
      // if we're sending linked models, create a nested structure
      // for the main model
      if (!isLinkedModel)
      {
        // create main model collection if it doesn't exist yet
        if (_mainModelCollection == null)
        {
          _mainModelCollection = new Collection(rootObject.name);
          rootObject.elements.Add(_mainModelCollection);
        }

        startingCollection = _mainModelCollection;
      }
      // for linked models
      else
      {
        // Use display name from settings if available, otherwise use original name
        string displayName = modelDisplayName ?? modelName;

        // Check if we already have a collection for this model display name
        if (!_linkedModelCollections.TryGetValue(displayName, out Collection? linkedModelCollection))
        {
          // First time seeing this model with this display name
          linkedModelCollection = new Collection(displayName);
          rootObject.elements.Add(linkedModelCollection);
          _linkedModelCollections[displayName] = linkedModelCollection;
        }

        startingCollection = linkedModelCollection;
      }
    }
    else
    {
      // if we don't have linked models, use the root directly
      startingCollection = rootObject;
    }

    // get the level and its properties
    string levelName = "No Level";
    if (element.LevelId != ElementId.InvalidElementId)
    {
      if (_levelCache.TryGetValue(element.LevelId, out var cachedLevel))
      {
        levelName = cachedLevel;
      }
      else
      {
        try
        {
          var level = (Level)doc.GetElement(element.LevelId);
          levelName = level.Name;
          _levelCache.Add(element.LevelId, levelName);
        }
        // the exception is swallowed since if an exception occurs, we fall back to "No Level" for the element
        catch (Exception e) when (!e.IsFatal()) { }
      }
    }

    // create path components. Currently, this is
    // level > category > type
    path.Add(levelName);
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

    // Use the collection's name for cache keys to ensure proper separation
    string modelIdentifier = startingCollection.name;

    // create a model-specific key for the collection cache
    string fullPathName = $"{modelIdentifier}:{string.Join(":", path)}";
    if (_collectionCache.TryGetValue(fullPathName, out Collection? value))
    {
      return value;
    }

    string flatPathName = modelIdentifier;
    Collection previousCollection = startingCollection;

    for (int i = 0; i < path.Count; i++)
    {
      var pathItem = path[i];
      flatPathName += ":" + pathItem;
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
