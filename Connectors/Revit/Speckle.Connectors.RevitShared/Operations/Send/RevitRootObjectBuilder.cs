using Autodesk.Revit.DB;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Extensions;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.DUI.Exceptions;
using Speckle.Connectors.DUI.Threading;
using Speckle.Connectors.Revit.HostApp;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Sdk;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.Revit.Operations.Send;

public class RevitRootObjectBuilder(
  IRootToSpeckleConverter converter,
  IConverterSettingsStore<RevitConversionSettings> converterSettings,
  ISendConversionCache sendConversionCache,
  ElementUnpacker elementUnpacker,
  SendCollectionManager sendCollectionManager,
  ILogger<RevitRootObjectBuilder> logger,
  RevitToSpeckleCacheSingleton revitToSpeckleCacheSingleton,
  IMainThreadContext mainThreadContext
) : IRootObjectBuilder<ElementId>
{
  // POC: SendSelection and RevitConversionContextStack should be interfaces, former needs interfaces

  public Task<RootObjectBuilderResult> Build(
    IReadOnlyList<ElementId> objects,
    SendInfo sendInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken ct = default
  ) =>
    mainThreadContext.RunOnMainThreadAsync(async () =>
    {
      var ret = BuildSync(objects, sendInfo, onOperationProgressed, ct);
      await Task.Delay(100, ct).ConfigureAwait(false);
      return ret;
    });

  private RootObjectBuilderResult BuildSync(
    IReadOnlyList<ElementId> objects,
    SendInfo sendInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken ct = default
  )
  {
    var doc = converterSettings.Current.Document;

    if (doc.IsFamilyDocument)
    {
      throw new SpeckleException("Family Environment documents are not supported.");
    }

    // 0 - Init the root
    Collection rootObject =
      new() { name = converterSettings.Current.Document.PathName.Split('\\').Last().Split('.').First() };
    rootObject["units"] = converterSettings.Current.SpeckleUnits;

    var revitElements = new List<Element>();

    // Convert ids to actual revit elements
    foreach (var id in objects)
    {
      var el = converterSettings.Current.Document.GetElement(id);
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
    var atomicObjects = elementUnpacker.UnpackSelectionForConversion(revitElements).ToList();

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
        if (sendConversionCache.TryGetValue(sendInfo.ProjectId, applicationId, out ObjectReference? value))
        {
          converted = value;
          cacheHitCount++;
        }
        else
        {
          converted = converter.Convert(revitElement);
          converted.applicationId = applicationId;
        }

        var collection = sendCollectionManager.GetAndCreateObjectHostCollection(revitElement, rootObject);

        collection.elements.Add(converted);
        results.Add(new(Status.SUCCESS, applicationId, sourceType, converted));
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        logger.LogSendConversionError(ex, sourceType);
        results.Add(new(Status.ERROR, applicationId, sourceType, null, ex));
      }

      onOperationProgressed.Report(new("Converting", (double)++countProgress / atomicObjects.Count));
    }

    if (results.All(x => x.Status == Status.ERROR))
    {
      throw new SpeckleException("Failed to convert all objects.");
    }

    var idsAndSubElementIds = elementUnpacker.GetElementsAndSubelementIdsFromAtomicObjects(atomicObjects);
    var materialProxies = revitToSpeckleCacheSingleton.GetRenderMaterialProxyListForObjects(idsAndSubElementIds);
    rootObject[ProxyKeys.RENDER_MATERIAL] = materialProxies;

    // NOTE: these are currently not used anywhere, we'll skip them until someone calls for it back
    // rootObject[ProxyKeys.PARAMETER_DEFINITIONS] = _parameterDefinitionHandler.Definitions;

    return new RootObjectBuilderResult(rootObject, results);
  }
}
