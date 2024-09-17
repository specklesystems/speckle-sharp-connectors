using System.Diagnostics;
using Autodesk.Revit.DB;
using Microsoft.Extensions.Logging;
using Revit.Async;
using Speckle.Connectors.DUI.Exceptions;
using Speckle.Connectors.Revit.HostApp;
using Speckle.Connectors.Utils.Builders;
using Speckle.Connectors.Utils.Caching;
using Speckle.Connectors.Utils.Conversion;
using Speckle.Connectors.Utils.Extensions;
using Speckle.Connectors.Utils.Operations;
using Speckle.Converters.Common;
using Speckle.Converters.Revit2023.ToSpeckle.Parameters;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Sdk;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.Revit.Operations.Send;

public class RevitRootObjectBuilder : IRootObjectBuilder<ElementId>
{
  // POC: SendSelection and RevitConversionContextStack should be interfaces, former needs interfaces
  private readonly IRootToSpeckleConverter _converter;
  private readonly IRevitConversionContextStack _conversionContextStack;
  private readonly Collection _rootObject;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly ElementUnpacker _elementUnpacker;
  private readonly SendCollectionManager _sendCollectionManager;
  private readonly ILogger<RevitRootObjectBuilder> _logger;
  private readonly ParameterDefinitionHandler _parameterDefinitionHandler;

  public RevitRootObjectBuilder(
    IRootToSpeckleConverter converter,
    IRevitConversionContextStack conversionContextStack,
    ISendConversionCache sendConversionCache,
    ElementUnpacker elementUnpacker,
    SendCollectionManager sendCollectionManager,
    ILogger<RevitRootObjectBuilder> logger,
    ParameterDefinitionHandler parameterDefinitionHandler
  )
  {
    _converter = converter;
    _conversionContextStack = conversionContextStack;
    _sendConversionCache = sendConversionCache;
    _elementUnpacker = elementUnpacker;
    _sendCollectionManager = sendCollectionManager;
    _logger = logger;
    _parameterDefinitionHandler = parameterDefinitionHandler;

    _rootObject = new Collection()
    {
      name = _conversionContextStack.Current.Document.PathName.Split('\\').Last().Split('.').First()
    };
  }

  public async Task<RootObjectBuilderResult> Build(
    IReadOnlyList<ElementId> objects,
    SendInfo sendInfo,
    Action<string, double?>? onOperationProgressed = null,
    CancellationToken ct = default
  )
  {
    var doc = _conversionContextStack.Current.Document;

    if (doc.IsFamilyDocument)
    {
      throw new SpeckleException("Family Environment documents are not supported.");
    }

    var revitElements = new List<Element>();

    // Convert ids to actual revit elements
    foreach (var id in objects)
    {
      var el = _conversionContextStack.Current.Document.GetElement(id);
      if (el != null)
      {
        revitElements.Add(el);
      }
    }

    if (revitElements.Count == 0)
    {
      throw new SpeckleSendFilterException("No objects were found. Please update your send filter!");
    }

    List<SendConversionResult> results = new(revitElements.Count);

    // Unpack groups (& other complex data structures)
    var atomicObjects = _elementUnpacker.UnpackSelectionForConversion(revitElements).ToList();

    var countProgress = 0;
    var cacheHitCount = 0;

    foreach (Element revitElement in atomicObjects)
    {
      ct.ThrowIfCancellationRequested();
      string applicationId = revitElement.UniqueId;
      string sourceType = revitElement.GetType().Name;
      try
      {
        Base converted;
        if (_sendConversionCache.TryGetValue(sendInfo.ProjectId, applicationId, out ObjectReference? value))
        {
          converted = value;
          cacheHitCount++;
        }
        else
        {
          converted = await RevitTask.RunAsync(() => _converter.Convert(revitElement)).ConfigureAwait(false);
          converted.applicationId = applicationId;
        }

        var collection = _sendCollectionManager.GetAndCreateObjectHostCollection(revitElement, _rootObject);
        collection.elements.Add(converted);
        results.Add(new(Status.SUCCESS, applicationId, sourceType, converted));
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogSendConversionError(ex, sourceType);
        results.Add(new(Status.ERROR, applicationId, sourceType, null, ex));
      }

      onOperationProgressed?.Invoke("Converting", (double)++countProgress / atomicObjects.Count);
    }

    if (results.All(x => x.Status == Status.ERROR))
    {
      throw new SpeckleConversionException("Failed to convert all objects.");
    }

    var idsAndSubElementIds = _elementUnpacker.GetElementsAndSubelementIdsFromAtomicObjects(atomicObjects);
    var materialProxies = _conversionContextStack.RenderMaterialProxyCache.GetRenderMaterialProxyListForObjects(
      idsAndSubElementIds
    );
    _rootObject[ProxyKeys.RENDER_MATERIAL] = materialProxies;

    _rootObject["definitions"] = _parameterDefinitionHandler.Definitions;

    Debug.WriteLine(
      $"Cache hit count {cacheHitCount} out of {objects.Count} ({(double)cacheHitCount / objects.Count})"
    );

    return new RootObjectBuilderResult(_rootObject, results);
  }
}
