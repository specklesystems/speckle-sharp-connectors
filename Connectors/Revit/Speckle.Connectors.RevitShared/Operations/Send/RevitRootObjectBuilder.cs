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
using Speckle.Sdk;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.Revit.Operations.Send;

public class RevitRootObjectBuilder : IRootObjectBuilder<ElementId>
{
  // POC: SendSelection and RevitConversionContextStack should be interfaces, former needs interfaces
  private readonly IRootToSpeckleConverter _converter;
  private readonly IRevitConversionContextStack _contextStack;
  private readonly Dictionary<string, Collection> _collectionCache;
  private readonly Collection _rootObject;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly ISyncToThread _syncToThread;
  private readonly SendSelectionUnpacker _sendSelectionUnpacker;
  private readonly SendCollectionManager _sendCollectionManager;
  private readonly SendMaterialManager _sendMaterialManager;

  public RevitRootObjectBuilder(
    IRootToSpeckleConverter converter,
    IRevitConversionContextStack contextStack,
    ISendConversionCache sendConversionCache,
    ISyncToThread syncToThread,
    SendSelectionUnpacker sendSelectionUnpacker,
    SendCollectionManager sendCollectionManager,
    SendMaterialManager sendMaterialManager
  )
  {
    _converter = converter;
    _contextStack = contextStack;
    _sendConversionCache = sendConversionCache;
    _syncToThread = syncToThread;
    _sendSelectionUnpacker = sendSelectionUnpacker;
    _sendCollectionManager = sendCollectionManager;
    _sendMaterialManager = sendMaterialManager;

    // Note, this class is instantiated per unit of work (aka per send operation), so we can safely initialize what we need in here.
    _collectionCache = new Dictionary<string, Collection>();
    _rootObject = new Collection()
    {
      name = _contextStack.Current.Document.PathName.Split('\\').Last().Split('.').First()
    };
  }

  public Task<RootObjectBuilderResult> Build(
    IReadOnlyList<ElementId> objects,
    SendInfo sendInfo,
    Action<string, double?>? onOperationProgressed = null,
    CancellationToken ct = default
  ) =>
    _syncToThread.RunOnThread(() =>
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
      var atomicObjects = _sendSelectionUnpacker.UnpackSelection(revitElements).ToList();

      var countProgress = 0; // because for(int i = 0; ...) loops are so last year
      var cacheHitCount = 0;
      List<SendConversionResult> results = new(revitElements.Count);

      foreach (Element revitElement in atomicObjects)
      {
        ct.ThrowIfCancellationRequested();
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

          var collection = _sendCollectionManager.GetAndCreateObjectHostCollection(revitElement, _rootObject);
          collection.elements.Add(converted);
          _sendMaterialManager.AddObjectToRenderMaterialMap(converted); // TODO: extract material into proxies here?

          results.Add(new(Status.SUCCESS, applicationId, revitElement.GetType().Name, converted));
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
          results.Add(new(Status.ERROR, applicationId, revitElement.GetType().Name, null, ex));
          // POC: add logging
        }

        onOperationProgressed?.Invoke("Converting", (double)++countProgress / revitElements.Count);
      }

      var materialProxies = _sendMaterialManager.RenderMaterialProxies.Values.ToList();
      var nextLevel = _contextStack.RenderMaterialProxies.Values.ToList();
      _rootObject["renderMaterialProxies"] = nextLevel;

      // POC: Log would be nice, or can be removed.
      Debug.WriteLine(
        $"Cache hit count {cacheHitCount} out of {objects.Count} ({(double)cacheHitCount / objects.Count})"
      );

      return new RootObjectBuilderResult(_rootObject, results);
    });
}
