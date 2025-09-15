using System.IO;
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
    // this should be the main model doc
    var doc = converterSettings.Current.Document;

    if (doc.IsFamilyDocument)
    {
      throw new SpeckleException("Family Environment documents are not supported.");
    }

    // init the root
    Collection rootObject =
      new()
      {
        name = converterSettings.Current.Document.PathName.Split('\\').Last().Split('.').First(),
        ["units"] = converterSettings.Current.SpeckleUnits
      };

    // instantiating some needed variables
    bool sendWithLinkedModels = converterSettings.Current.SendLinkedModels;
    List<SendConversionResult> results = [];

    // sorting documents and preparing names for collection (if necessary)
    var (mainModel, linkedModelDocs) = linkedModelHandler.GroupDocumentsByUniqueModels(documentElementContexts);
    if (mainModel == null)
    {
      throw new SpeckleException("Main Model not found.");
    }

    if (linkedModelDocs.Count != 0 && sendWithLinkedModels)
    {
      linkedModelHandler.PrepareLinkedModelNames(documentElementContexts);
    }

    // filter and validate documents
    var validDocumentsToConvert = FilterValidDocuments(mainModel, linkedModelDocs, sendWithLinkedModels, results);
    if (validDocumentsToConvert.Count == 0)
    {
      throw new SpeckleSendFilterException("No objects were found. Please update your publish filter!");
    }

    var atomicObjectsByDocumentAndTransform = UnpackDocuments(validDocumentsToConvert);

    // convert objects
    ConvertObjects(
      atomicObjectsByDocumentAndTransform,
      projectId,
      rootObject,
      sendWithLinkedModels,
      onOperationProgressed,
      results,
      out var flatElements,
      cancellationToken
    );

    // add proxies to root
    AddProxiesToRoot(rootObject, flatElements);

    return new RootObjectBuilderResult(rootObject, results);
  }

  private List<DocumentToConvert> UnpackDocuments(List<DocumentToConvert> documentsToConvert)
  {
    var atomicObjectsByDocumentAndTransform = new List<DocumentToConvert>();

    foreach (var documentToConvert in documentsToConvert)
    {
      using (converterSettings.Push(currentSettings => currentSettings with { Document = documentToConvert.Doc }))
      {
        var atomicObjects = elementUnpacker
          .UnpackSelectionForConversion(documentToConvert.Elements, documentToConvert.Doc)
          .ToList();
        atomicObjectsByDocumentAndTransform.Add(documentToConvert with { Elements = atomicObjects });
      }
    }

    return atomicObjectsByDocumentAndTransform;
  }

  private void ConvertObjects(
    List<DocumentToConvert> atomicObjectsByDocumentAndTransform,
    string projectId,
    Collection rootObject,
    bool sendWithLinkedModels,
    IProgress<CardProgress> onOperationProgressed,
    List<SendConversionResult> results,
    out List<Element> flatElements,
    CancellationToken cancellationToken
  )
  {
    var countProgress = 0;
    var skippedObjectCount = 0;
    var atomicObjectCount = atomicObjectsByDocumentAndTransform.Sum(d => d.Elements.Count);

    foreach (var atomicObjectByDocumentAndTransform in atomicObjectsByDocumentAndTransform)
    {
      string? modelDisplayName = null;
      if (atomicObjectByDocumentAndTransform.Doc.IsLinked)
      {
        string id = linkedModelHandler.GetIdFromDocumentToConvert(atomicObjectByDocumentAndTransform);
        linkedModelHandler.LinkedModelDisplayNames.TryGetValue(id, out modelDisplayName);
      }

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

            // Cache lookup for non-transformed elements
            if (!hasTransform && sendConversionCache.TryGetValue(projectId, applicationId, out ObjectReference? value))
            {
              converted = value;
            }
            else
            {
              // Apply transform hash for transformed elements
              if (hasTransform)
              {
                string transformHash = linkedModelHandler.GetTransformHash(
                  atomicObjectByDocumentAndTransform.Transform.NotNull()
                );
                applicationId = $"{applicationId}_t{transformHash}";
              }

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

    // Validation
    if (skippedObjectCount == atomicObjectCount)
    {
      throw new SpeckleException("No supported objects visible. Update publish filter or check publish settings.");
    }

    if (results.All(x => x.Status == Status.ERROR))
    {
      throw new SpeckleException("Failed to convert all objects.");
    }

    flatElements = atomicObjectsByDocumentAndTransform.SelectMany(t => t.Elements).ToList();
  }

  // Helper method to filter and validate documents
  private List<DocumentToConvert> FilterValidDocuments(
    DocumentToConvert mainModel,
    Dictionary<string, List<DocumentToConvert>> linkedModelDocs,
    bool sendWithLinkedModels,
    List<SendConversionResult> results
  )
  {
    var validDocuments = new List<DocumentToConvert>();

    // Add main models (always included)
    var validMainDocElements = FilterValidElements(mainModel.Elements);
    if (validMainDocElements.Count > 0)
    {
      validDocuments.Add(mainModel with { Elements = validMainDocElements });
    }

    // Add linked models based on settings
    foreach (var linkedModelDoc in linkedModelDocs)
    {
      foreach (var linkedModelInstance in linkedModelDoc.Value)
      {
        // Add warnings for disabled linked models
        if (!sendWithLinkedModels)
        {
          results.Add(
            new(
              Status.WARNING,
              linkedModelInstance.Doc.PathName,
              typeof(RevitLinkInstance).ToString(),
              null,
              new SpeckleException("Enable linked model support from the settings to send this object")
            )
          );
          continue;
        }

        var validElements = FilterValidElements(linkedModelInstance.Elements);
        if (validElements.Count > 0)
        {
          validDocuments.Add(linkedModelInstance with { Elements = validElements });
        }
      }
    }

    return validDocuments;
  }

  // Helper method to filter valid elements
  private List<Element> FilterValidElements(List<Element> elements)
  {
    var validElements = new List<Element>();
    foreach (var element in elements)
    {
      if (element != null && element.Category != null)
      {
        validElements.Add(element);
      }
    }
    return validElements;
  }

  // Extract proxy logic
  private void AddProxiesToRoot(Collection rootObject, List<Element> flatElements)
  {
    var idsAndSubElementIds = elementUnpacker.GetElementsAndSubelementIdsFromAtomicObjects(flatElements);

    var renderMaterialProxies = revitToSpeckleCacheSingleton.GetRenderMaterialProxyListForObjects(idsAndSubElementIds);
    rootObject[ProxyKeys.RENDER_MATERIAL] = renderMaterialProxies;

    var levelProxies = levelUnpacker.Unpack(flatElements);
    rootObject[ProxyKeys.LEVEL] = levelProxies;

    // Store reference point transform if available
    if (converterSettings.Current.ReferencePointTransform is Transform transform)
    {
      var transformMatrix = ReferencePointHelper.CreateTransformDataForRootObject(transform);
      rootObject[ReferencePointHelper.REFERENCE_POINT_TRANSFORM_KEY] = transformMatrix;
    }
  }
}
