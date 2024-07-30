using System.Diagnostics;
using Autodesk.Revit.DB;
using Speckle.Connectors.DUI.Exceptions;
using Speckle.Connectors.Revit.HostApp;
using Speckle.Connectors.Utils.Builders;
using Speckle.Connectors.Utils.Caching;
using Speckle.Connectors.Utils.Conversion;
using Speckle.Connectors.Utils.Operations;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Core.Logging;
using Speckle.Core.Models;
using Speckle.Core.Models.Collections;

namespace Speckle.Connectors.Revit.Operations.Send;

public class RevitRootObjectBuilder : IRootObjectBuilder<ElementId>
{
  // POC: SendSelection and RevitConversionContextStack should be interfaces, former needs interfaces
  private readonly IRootToSpeckleConverter _converter;
  private readonly IRevitConversionContextStack _contextStack;
  private readonly Dictionary<string, Collection> _collectionCache;
  private readonly Collection _rootObject;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly SendSelectionUnpacker _sendSelectionUnpacker;

  public RevitRootObjectBuilder(
    IRootToSpeckleConverter converter,
    IRevitConversionContextStack contextStack,
    ISendConversionCache sendConversionCache,
    SendSelectionUnpacker sendSelectionUnpacker
  )
  {
    _converter = converter;
    _contextStack = contextStack;
    _sendConversionCache = sendConversionCache;
    _sendSelectionUnpacker = sendSelectionUnpacker;
    // Note, this class is instantiated per unit of work (aka per send operation), so we can safely initialize what we need in here.
    _collectionCache = new Dictionary<string, Collection>();
    _rootObject = new Collection()
    {
      name = _contextStack.Current.Document.PathName.Split('\\').Last().Split('.').First()
    };
  }

  public RootObjectBuilderResult Build(
    IReadOnlyList<ElementId> objects,
    SendInfo sendInfo,
    Action<string, double?>? onOperationProgressed = null,
    CancellationToken ct = default
  )
  {
    var doc = _contextStack.Current.Document;

    if (doc.IsFamilyDocument)
    {
      throw new SpeckleException("Family Environment documents are not supported.");
    }

    var revitElements = new List<Element>();

    // Convert ids to actual revit elements
    foreach (var id in objects)
    {
      var el = _contextStack.Current.Document.GetElement(id);
      if (el != null)
      {
        revitElements.Add(el);
      }
    }

    if (revitElements.Count == 0)
    {
      throw new SpeckleSendFilterException("No objects were found. Please update your send filter!");
    }

    // Unpack groups (& other complex data structures)
    var atomicObjects = _sendSelectionUnpacker.UnpackSelection(revitElements);

    var countProgress = 0; // because for(int i = 0; ...) loops are so last year
    var cacheHitCount = 0;
    List<SendConversionResult> results = new(revitElements.Count);
    var path = new string[2];

    foreach (Element revitElement in atomicObjects)
    {
      ct.ThrowIfCancellationRequested();

      var cat = revitElement.Category.Name;
      path[0] = doc.GetElement(revitElement.LevelId) is not Level level ? "No level" : level.Name;
      path[1] = cat;
      var collection = GetAndCreateObjectHostCollection(path);

      var applicationId = revitElement.Id.ToString();
      try
      {
        Base converted;
        if (_sendConversionCache.TryGetValue(sendInfo.ProjectId, applicationId, out ObjectReference value))
        {
          converted = value;
          cacheHitCount++;
        }
        else
        {
          converted = _converter.Convert(revitElement);
          converted.applicationId = applicationId;
        }

        collection.elements.Add(converted);
        results.Add(new(Status.SUCCESS, applicationId, revitElement.GetType().Name, converted));
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        results.Add(new(Status.ERROR, applicationId, revitElement.GetType().Name, null, ex));
        // POC: add logging
      }

      onOperationProgressed?.Invoke("Converting", (double)++countProgress / revitElements.Count);
    }

    // POC: Log would be nice, or can be removed.
    Debug.WriteLine(
      $"Cache hit count {cacheHitCount} out of {objects.Count} ({(double)cacheHitCount / objects.Count})"
    );

    return new(_rootObject, results);
  }

  /// <summary>
  /// Creates and nests collections based on the provided path within the root collection provided. This will not return a new collection each time is called, but an existing one if one is found.
  /// For example, you can use this to use (or re-use) a new collection for a path of (level, category) as it's currently implemented.
  /// </summary>
  /// <param name="path"></param>
  /// <returns></returns>
  private Collection GetAndCreateObjectHostCollection(string[] path)
  {
    string fullPathName = string.Concat(path);
    if (_collectionCache.TryGetValue(fullPathName, out Collection? value))
    {
      return value;
    }

    string flatPathName = "";
    Collection previousCollection = _rootObject;

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
        childCollection = new Collection(pathItem, "layer");
        previousCollection.elements.Add(childCollection);
        _collectionCache[flatPathName] = childCollection;
      }

      previousCollection = childCollection;
    }

    return previousCollection;
  }
}
