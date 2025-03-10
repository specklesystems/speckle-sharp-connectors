using Autodesk.Revit.DB;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Extensions;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Exceptions;
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
  IThreadContext threadContext,
  SendCollectionManager sendCollectionManager,
  ILogger<RevitRootObjectBuilder> logger,
  RevitToSpeckleCacheSingleton revitToSpeckleCacheSingleton
) : IRootObjectBuilder<DocumentToConvert>
{
  // POC: SendSelection and RevitConversionContextStack should be interfaces, former needs interfaces

  public Task<RootObjectBuilderResult> Build(
    IReadOnlyList<DocumentToConvert> documentElementContexts,
    SendInfo sendInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken ct = default
  ) =>
    threadContext.RunOnMainAsync(
      () => Task.FromResult(BuildSync(documentElementContexts, sendInfo, onOperationProgressed, ct))
    );

  private RootObjectBuilderResult BuildSync(
    IReadOnlyList<DocumentToConvert> documentElementContexts,
    SendInfo sendInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
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

    var filteredDocumentsToConvert = new List<DocumentToConvert>();
    List<SendConversionResult> results = new();

    foreach (var documentElementContext in documentElementContexts)
    {
      var elementsInTransform = new List<Element>();
      foreach (var el in documentElementContext.Elements)
      {
        if (el == null)
        {
          continue;
        }

        if (el.Category == null)
        {
          continue;
        }

        elementsInTransform.Add(el);
      }
      filteredDocumentsToConvert.Add(documentElementContext with { Elements = elementsInTransform });
    }

    // TODO: check the exception!!!!
    if (filteredDocumentsToConvert.Count == 0)
    {
      throw new SpeckleSendFilterException("No objects were found. Please update your publish filter!");
    }

    // Unpack groups (& other complex data structures)
    var atomicObjectsByDocumentAndTransform = new List<DocumentToConvert>();
    var atomicObjectCount = 0;
    foreach (var filteredDocumentToConvert in filteredDocumentsToConvert)
    {
      using (
        converterSettings.Push(currentSettings => currentSettings with { Document = filteredDocumentToConvert.Doc })
      )
      {
        var atomicObjects = elementUnpacker.UnpackSelectionForConversion(filteredDocumentToConvert.Elements).ToList();
        atomicObjectsByDocumentAndTransform.Add(filteredDocumentToConvert with { Elements = atomicObjects });
        atomicObjectCount += atomicObjects.Count;
      }
    }

    var countProgress = 0;
    var cacheHitCount = 0;
    var skippedObjectCount = 0;

    foreach (var atomicObjectByDocumentAndTransform in atomicObjectsByDocumentAndTransform)
    {
      // if user doesn't have send linked models enabled, don't convert ...
      if (atomicObjectByDocumentAndTransform.Doc.IsLinked && !converterSettings.Current.SendLinkedModels)
      {
        results.Add(
          new(
            Status.WARNING,
            atomicObjectByDocumentAndTransform.Doc.PathName, // TODO: User won't be able to highlight linked model from report.
            typeof(RevitLinkInstance).ToString(),
            null,
            new SpeckleException("Enable linked model support from the settings to send this object")
          )
        );
        continue;
      }

      // here we do magic for changing the transform and the related document according to model. first one is always the main model.
      using (
        converterSettings.Push(currentSettings =>
          currentSettings with
          {
            ReferencePointTransform = atomicObjectByDocumentAndTransform.Transform,
            Document = atomicObjectByDocumentAndTransform.Doc
          }
        )
      )
      {
        var atomicObjects = atomicObjectByDocumentAndTransform.Elements;
        foreach (Element revitElement in atomicObjects)
        {
          cancellationToken.ThrowIfCancellationRequested();
          string applicationId = revitElement.UniqueId;
          string sourceType = revitElement.GetType().Name;
          try
          {
            if (!SupportedCategoriesUtils.IsSupportedCategory(revitElement.Category))
            {
              var cat = revitElement.Category != null ? revitElement.Category.Name : "No category";
              results.Add(
                new(
                  Status.WARNING,
                  revitElement.UniqueId,
                  cat,
                  null,
                  new SpeckleException($"Category {cat} is not supported.")
                )
              );
              skippedObjectCount++;
              continue;
            }

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

          onOperationProgressed.Report(new("Converting", (double)++countProgress / atomicObjectCount));
        }
      }
    }

    if (results.All(x => x.Status == Status.ERROR) || skippedObjectCount == atomicObjectCount)
    {
      throw new SpeckleException("Failed to convert all objects.");
    }

    var idsAndSubElementIds = elementUnpacker.GetElementsAndSubelementIdsFromAtomicObjects(
      atomicObjectsByDocumentAndTransform.SelectMany(t => t.Elements).ToList()
    );
    var renderMaterialProxies = revitToSpeckleCacheSingleton.GetRenderMaterialProxyListForObjects(idsAndSubElementIds);
    rootObject[ProxyKeys.RENDER_MATERIAL] = renderMaterialProxies;

    // NOTE: these are currently not used anywhere, we'll skip them until someone calls for it back
    // rootObject[ProxyKeys.PARAMETER_DEFINITIONS] = _parameterDefinitionHandler.Definitions;

    return new RootObjectBuilderResult(rootObject, results);
  }
}
