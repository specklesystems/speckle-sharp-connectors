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
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.Revit.Operations.Send;

public class RevitRootObjectBuilder(
  IRootToSpeckleConverter converter,
  IConverterSettingsStore<RevitConversionSettings> converterSettings,
  ISendConversionCache sendConversionCache,
  ElementUnpacker elementUnpacker,
  LevelUnpacker levelUnpacker,
  ViewUnpacker viewUnpacker,
  IThreadContext threadContext,
  SendCollectionManager sendCollectionManager,
  ILogger<RevitRootObjectBuilder> logger,
  RevitToSpeckleCacheSingleton revitToSpeckleCacheSingleton,
  LinkedModelHandler linkedModelHandler
) : IRootObjectBuilder<DocumentToConvert>
{
  public Task<RootObjectBuilderResult> Build(
    IReadOnlyList<DocumentToConvert> documentElementContexts,
    string projectId,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken ct = default
  ) =>
    threadContext.RunOnMainAsync(
      () => Task.FromResult(BuildSync(documentElementContexts, projectId, onOperationProgressed, ct))
    );

  private RootObjectBuilderResult BuildSync(
    IReadOnlyList<DocumentToConvert> documentElementContexts,
    string projectId,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    var doc = converterSettings.Current.Document;

    if (doc.IsFamilyDocument)
    {
      throw new SpeckleException("Family Environment documents are not supported.");
    }

    // init the root
    Collection rootObject =
      new() { name = converterSettings.Current.Document.PathName.Split('\\').Last().Split('.').First() };
    rootObject["units"] = converterSettings.Current.SpeckleUnits;

    var filteredDocumentsToConvert = new List<DocumentToConvert>();
    bool sendWithLinkedModels = converterSettings.Current.SendLinkedModels;
    List<SendConversionResult> results = new();

    // Prepare linked model display names if needed
    if (sendWithLinkedModels)
    {
      linkedModelHandler.PrepareLinkedModelNames(documentElementContexts);
    }

    foreach (var documentElementContext in documentElementContexts)
    {
      // add appropriate warnings for linked documents
      if (documentElementContext.Doc.IsLinked && !sendWithLinkedModels)
      {
        results.Add(
          new(
            Status.WARNING,
            documentElementContext.Doc.PathName,
            typeof(RevitLinkInstance).ToString(),
            null,
            new SpeckleException("Enable linked model support from the settings to send this object")
          )
        );
        continue;
      }

      // filter for valid elements
      // if send linked models setting is disabled List<Elements> will be empty, and we won't enter foreach loop
      var elementsInTransform = new List<Element>();
      foreach (var el in documentElementContext.Elements)
      {
        if (el == null || el.Category == null)
        {
          continue;
        }
        elementsInTransform.Add(el);
      }

      // only add contexts with elements
      if (elementsInTransform.Count > 0)
      {
        filteredDocumentsToConvert.Add(documentElementContext with { Elements = elementsInTransform });
      }
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
        var atomicObjects = elementUnpacker
          .UnpackSelectionForConversion(filteredDocumentToConvert.Elements, filteredDocumentToConvert.Doc)
          .ToList();
        atomicObjectsByDocumentAndTransform.Add(filteredDocumentToConvert with { Elements = atomicObjects });
        atomicObjectCount += atomicObjects.Count;
      }
    }

    var countProgress = 0;
    var cacheHitCount = 0;
    var skippedObjectCount = 0;

    foreach (var atomicObjectByDocumentAndTransform in atomicObjectsByDocumentAndTransform)
    {
      string? modelDisplayName = null;
      if (atomicObjectByDocumentAndTransform.Doc.IsLinked)
      {
        string id = linkedModelHandler.GetIdFromDocumentToConvert(atomicObjectByDocumentAndTransform);
        linkedModelHandler.LinkedModelDisplayNames.TryGetValue(id, out modelDisplayName);
      }

      // here we do magic for changing the transform and the related document according to model. first one is always the main model.
      using (
        converterSettings.Push(currentSettings =>
          currentSettings with
          {
            ReferencePointTransform = atomicObjectByDocumentAndTransform.Transform,
            Document = atomicObjectByDocumentAndTransform.Doc,
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
            bool hasTransform = atomicObjectByDocumentAndTransform.Transform != null;

            // non-transformed elements can safely rely on cache
            // TODO: Potential here to transform cached objects and NOT reconvert,
            // TODO: we wont do !hasTransform here, and re-set application id before this

            if (!hasTransform && sendConversionCache.TryGetValue(projectId, applicationId, out ObjectReference? value))
            {
              converted = value;
              cacheHitCount++;
            }
            // not in cache means we convert
            else
            {
              // if it has a transform we append transform hash to the applicationId to distinguish the elements from other instances
              if (hasTransform)
              {
                string transformHash = linkedModelHandler.GetTransformHash(
                  atomicObjectByDocumentAndTransform.Transform.NotNull()
                );
                applicationId = $"{applicationId}_t{transformHash}";
              }
              // normal conversions
              converted = converter.Convert(revitElement);
              converted.applicationId = applicationId;
            }

            var collection = sendCollectionManager.GetAndCreateObjectHostCollection(
              revitElement,
              rootObject,
              sendWithLinkedModels,
              modelDisplayName
            );

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

    // if we ended up skipping everything, there is a reason for this, that users can diagnose themselves
    // this can occur if a published view contains only unsupported objects or if user trying to ONLY send linked model
    // docs but the setting is disabled
    if (skippedObjectCount == atomicObjectCount)
    {
      throw new SpeckleException("No supported objects visible. Update publish filter or check publish settings.");
    }

    // this is, I suppose, fully on us?
    if (results.All(x => x.Status == Status.ERROR))
    {
      throw new SpeckleException("Failed to convert all objects.");
    }

    // STEP 5: Unpack proxies to attach to root collection
    var flatElements = atomicObjectsByDocumentAndTransform.SelectMany(t => t.Elements).ToList();
    var idsAndSubElementIds = elementUnpacker.GetElementsAndSubelementIdsFromAtomicObjects(flatElements);

    var renderMaterialProxies = revitToSpeckleCacheSingleton.GetRenderMaterialProxyListForObjects(idsAndSubElementIds);
    rootObject[ProxyKeys.RENDER_MATERIAL] = renderMaterialProxies;

    var levelProxies = levelUnpacker.Unpack(flatElements);
    rootObject[ProxyKeys.LEVEL] = levelProxies;

    rootObject[ProxyKeys.INSTANCE_DEFINITION] = revitToSpeckleCacheSingleton.GetInstanceDefinitionProxiesForObjects(
      idsAndSubElementIds
    );
    rootObject.elements.Add(
      new Collection()
      {
        elements = revitToSpeckleCacheSingleton.GetBaseObjectsForObjects(idsAndSubElementIds),
        name = "revitInstancedObjects"
      }
    );

    // STEP 6: Unpack all other objects to attach to root collection
    var views = viewUnpacker.Unpack(converterSettings.Current.Document);
    rootObject[RootKeys.VIEW] = views;

    // NOTE: these are currently not used anywhere, we'll skip them until someone calls for it back
    // rootObject[ProxyKeys.PARAMETER_DEFINITIONS] = _parameterDefinitionHandler.Definitions;

    // we want to store transform data for chosen reference point setting
    if (converterSettings.Current.ReferencePointTransform is Transform transform)
    {
      var transformMatrix = ReferencePointHelper.CreateTransformDataForRootObject(transform);
      rootObject[RootKeys.REFERENCE_POINT] = transformMatrix;
    }

    return new RootObjectBuilderResult(rootObject, results);
  }
}
