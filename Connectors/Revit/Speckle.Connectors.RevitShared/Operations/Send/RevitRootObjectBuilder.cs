using Autodesk.Revit.DB;
using Microsoft.Extensions.Logging;
using Revit.Async;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Extensions;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.DUI.Exceptions;
using Speckle.Connectors.Revit.HostApp;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Converters.RevitShared.ToSpeckle;
using Speckle.Sdk;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.Revit.Operations.Send;

public class RevitRootObjectBuilder : IRootObjectBuilder<ElementId>
{
  // POC: SendSelection and RevitConversionContextStack should be interfaces, former needs interfaces
  private readonly IRootToSpeckleConverter _converter;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly ElementUnpacker _elementUnpacker;
  private readonly SendCollectionManager _sendCollectionManager;
  private readonly RevitToSpeckleCacheSingleton _revitToSpeckleCacheSingleton;
  private readonly ILogger<RevitRootObjectBuilder> _logger;
  private readonly ParameterDefinitionHandler _parameterDefinitionHandler;

  public RevitRootObjectBuilder(
    IRootToSpeckleConverter converter,
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    ISendConversionCache sendConversionCache,
    ElementUnpacker elementUnpacker,
    SendCollectionManager sendCollectionManager,
    ILogger<RevitRootObjectBuilder> logger,
    ParameterDefinitionHandler parameterDefinitionHandler,
    RevitToSpeckleCacheSingleton revitToSpeckleCacheSingleton
  )
  {
    _converter = converter;
    _converterSettings = converterSettings;
    _sendConversionCache = sendConversionCache;
    _elementUnpacker = elementUnpacker;
    _sendCollectionManager = sendCollectionManager;
    _revitToSpeckleCacheSingleton = revitToSpeckleCacheSingleton;
    _logger = logger;
    _parameterDefinitionHandler = parameterDefinitionHandler;
  }

  public async Task<RootObjectBuilderResult> Build(
    IReadOnlyList<ElementId> objects,
    SendInfo sendInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken ct = default
  ) => await RevitTask.RunAsync(() => BuildSync(objects, sendInfo, onOperationProgressed, ct)).ConfigureAwait(false);

  private RootObjectBuilderResult BuildSync(
    IReadOnlyList<ElementId> objects,
    SendInfo sendInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken ct = default
  )
  {
    var doc = _converterSettings.Current.Document;

    if (doc.IsFamilyDocument)
    {
      throw new SpeckleException("Family Environment documents are not supported.");
    }

    // 0 - Init the root
    Collection rootObject =
      new() { name = _converterSettings.Current.Document.PathName.Split('\\').Last().Split('.').First() };
    rootObject["units"] = _converterSettings.Current.SpeckleUnits;

    var revitElements = new List<Element>();

    // Convert ids to actual revit elements
    foreach (var id in objects)
    {
      var el = _converterSettings.Current.Document.GetElement(id);
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
          var conversionResult = _converter.Convert(revitElement);
          if (conversionResult.IsFailure)
          {
            results.Add(new(Status.ERROR, applicationId,sourceType,  conversionResult.Message));
            continue;
          }
          converted = conversionResult.Value;
          converted.applicationId = applicationId;
        }

        var collection = _sendCollectionManager.GetAndCreateObjectHostCollection(revitElement, rootObject);

        collection.elements.Add(converted);
        results.Add(new(Status.SUCCESS, applicationId, sourceType, converted, null));
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogSendConversionError(ex, sourceType);
        results.Add(new(Status.ERROR, applicationId, sourceType, null, ex));
      }

      onOperationProgressed.Report(new("Converting", (double)++countProgress / atomicObjects.Count));
    }

    if (results.All(x => x.Status == Status.ERROR))
    {
      throw new SpeckleException("Failed to convert all objects.");
    }

    var idsAndSubElementIds = _elementUnpacker.GetElementsAndSubelementIdsFromAtomicObjects(atomicObjects);
    var materialProxies = _revitToSpeckleCacheSingleton.GetRenderMaterialProxyListForObjects(idsAndSubElementIds);
    rootObject[ProxyKeys.RENDER_MATERIAL] = materialProxies;

    // NOTE: these are currently not used anywhere, we'll skip them until someone calls for it back
    // rootObject[ProxyKeys.PARAMETER_DEFINITIONS] = _parameterDefinitionHandler.Definitions;

    return new RootObjectBuilderResult(rootObject, results);
  }
}
