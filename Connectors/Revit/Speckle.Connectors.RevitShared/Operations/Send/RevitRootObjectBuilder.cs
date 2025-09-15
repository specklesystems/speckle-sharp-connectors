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
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Instances;

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
    var (validMainModel, validLinkedModelGroups) = FilterValidDocuments(
      mainModel,
      linkedModelDocs,
      sendWithLinkedModels,
      results
    );
    if (validMainModel.Elements.Count == 0 && validLinkedModelGroups.Count == 0)
    {
      throw new SpeckleSendFilterException("No objects were found. Please update your publish filter!");
    }

    ConvertObjects(
      validMainModel,
      validLinkedModelGroups,
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

  private void ConvertObjects(
    DocumentToConvert mainModel,
    Dictionary<string, List<DocumentToConvert>> linkedModelGroups,
    string projectId,
    Collection rootObject,
    bool sendWithLinkedModels,
    IProgress<CardProgress> onOperationProgressed,
    List<SendConversionResult> results,
    out List<Element> flatElements,
    CancellationToken cancellationToken
  )
  {
    var allFlatElements = new List<Element>();
    var countProgress = 0;
    var skippedObjectCount = 0;
    var totalElementCount = 0;

    // track linked model conversion results for proxy creation
    var linkedModelConversionResults = new List<LinkedModelConversionResult>();

    // calculate total elements for progress reporting
    totalElementCount += mainModel.Elements.Count;
    foreach (var group in linkedModelGroups.Values)
    {
      totalElementCount += group.First().Elements.Count; // only count unique model once
    }

    // convert main model
    var unpackedMainModel = UnpackDocument(mainModel);
    allFlatElements.AddRange(unpackedMainModel.Elements);

    ConvertDocumentElements(
      unpackedMainModel,
      projectId,
      rootObject,
      sendWithLinkedModels,
      onOperationProgressed,
      results,
      ref countProgress,
      ref skippedObjectCount,
      totalElementCount,
      null,
      cancellationToken
    );

    // convert unique linked models
    if (sendWithLinkedModels)
    {
      foreach (var linkedModelGroup in linkedModelGroups)
      {
        string documentPath = linkedModelGroup.Key;
        var instances = linkedModelGroup.Value;

        // create result tracking object
        var conversionResult = new LinkedModelConversionResult(documentPath, instances);
        linkedModelConversionResults.Add(conversionResult);

        // convert only the FIRST instance of each unique linked model (without its transform)
        var firstInstance = instances.First();
        var uniqueModelToConvert = firstInstance with { Transform = null }; // remove transform
        var unpackedUniqueModel = UnpackDocument(uniqueModelToConvert);
        allFlatElements.AddRange(unpackedUniqueModel.Elements);

        ConvertDocumentElements(
          unpackedUniqueModel,
          projectId,
          rootObject,
          sendWithLinkedModels,
          onOperationProgressed,
          results,
          ref countProgress,
          ref skippedObjectCount,
          totalElementCount,
          conversionResult,
          cancellationToken
        );
      }

      // create instance definition proxies
      CreateInstanceDefinitionProxies(linkedModelConversionResults, rootObject);
    }

    // validation
    if (skippedObjectCount == totalElementCount)
    {
      throw new SpeckleException("No supported objects visible. Update publish filter or check publish settings.");
    }

    if (results.All(x => x.Status == Status.ERROR))
    {
      throw new SpeckleException("Failed to convert all objects.");
    }

    flatElements = allFlatElements;
  }

  // helper method to filter and validate documents
  private (
    DocumentToConvert MainModel,
    Dictionary<string, List<DocumentToConvert>> LinkedModelGroups
  ) FilterValidDocuments(
    DocumentToConvert mainModel,
    Dictionary<string, List<DocumentToConvert>> linkedModelGroups,
    bool sendWithLinkedModels,
    List<SendConversionResult> results
  )
  {
    var validLinkedModelGroups = new Dictionary<string, List<DocumentToConvert>>();

    // filter main model
    var validMainModelElements = FilterValidElements(mainModel.Elements);
    if (validMainModelElements.Count > 0)
    {
      mainModel = mainModel with { Elements = validMainModelElements };
    }

    // filter linked models
    foreach (var linkedModelGroup in linkedModelGroups)
    {
      if (!sendWithLinkedModels)
      {
        // add warnings for disabled linked models
        foreach (var instance in linkedModelGroup.Value)
        {
          results.Add(
            new(
              Status.WARNING,
              instance.Doc.PathName,
              typeof(RevitLinkInstance).ToString(),
              null,
              new SpeckleException("Enable linked model support from the settings to send this object")
            )
          );
        }
        continue;
      }

      // for linked models, we only need to validate the first instance (since we convert unique models once)
      var firstInstance = linkedModelGroup.Value.First();
      var validElements = FilterValidElements(firstInstance.Elements);

      if (validElements.Count > 0)
      {
        // keep all instances but update the first one with valid elements
        var validInstances = linkedModelGroup.Value.ToList();
        validInstances[0] = validInstances[0] with { Elements = validElements };
        validLinkedModelGroups[linkedModelGroup.Key] = validInstances;
      }
    }

    return (mainModel, validLinkedModelGroups);
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

  private DocumentToConvert UnpackDocument(DocumentToConvert documentToConvert)
  {
    using (converterSettings.Push(currentSettings => currentSettings with { Document = documentToConvert.Doc }))
    {
      var atomicObjects = elementUnpacker
        .UnpackSelectionForConversion(documentToConvert.Elements, documentToConvert.Doc)
        .ToList();
      return documentToConvert with { Elements = atomicObjects };
    }
  }

  // Helper method to convert elements from a single document
  private void ConvertDocumentElements(
    DocumentToConvert documentToConvert,
    string projectId,
    Collection rootObject,
    bool sendWithLinkedModels,
    IProgress<CardProgress> onOperationProgressed,
    List<SendConversionResult> results,
    ref int countProgress,
    ref int skippedObjectCount,
    int totalElementCount,
    LinkedModelConversionResult? trackingResult,
    CancellationToken cancellationToken
  )
  {
    string? modelDisplayName = null;
    if (documentToConvert.Doc.IsLinked)
    {
      string id = linkedModelHandler.GetIdFromDocumentToConvert(documentToConvert);
      linkedModelHandler.LinkedModelDisplayNames.TryGetValue(id, out modelDisplayName);
    }

    // IMPORTANT: For linked models, we're NOT applying the instance transform here
    // The transform will be applied later in instance proxies
    using (
      converterSettings.Push(currentSettings =>
        currentSettings with
        {
          ReferencePointTransform = documentToConvert.Transform, // this will be null for linked models now
          Document = documentToConvert.Doc,
        }
      )
    )
    {
      var atomicObjects = documentToConvert.Elements;
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
          bool hasTransform = documentToConvert.Transform != null;

          if (!hasTransform && sendConversionCache.TryGetValue(projectId, applicationId, out ObjectReference? value))
          {
            converted = value;
          }
          else
          {
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

          if (trackingResult != null && converted.applicationId != null)
          {
            trackingResult.ConvertedElementIds.Add(converted.applicationId);
          }
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
          logger.LogSendConversionError(ex, sourceType);
          results.Add(new(Status.ERROR, applicationId, sourceType, null, ex));
        }

        onOperationProgressed.Report(new("Converting", (double)++countProgress / totalElementCount));
      }
    }
  }

  private void CreateInstanceDefinitionProxies(
    List<LinkedModelConversionResult> linkedModelConversionResults,
    Collection rootObject
  )
  {
    var instanceDefinitionProxies = new List<InstanceDefinitionProxy>();

    foreach (var conversionResult in linkedModelConversionResults)
    {
      if (conversionResult.ConvertedElementIds.Count == 0)
      {
        // Skip linked models with no converted elements
        continue;
      }

      // Create a unique definition ID for this linked model
      string definitionId = GenerateDefinitionId(conversionResult.DocumentPath);
      string modelName = Path.GetFileNameWithoutExtension(conversionResult.DocumentPath);

      logger.LogInformation(
        "Creating InstanceDefinitionProxy '{DefinitionId}' for linked model '{ModelName}' with {ElementCount} elements",
        definitionId,
        modelName,
        conversionResult.ConvertedElementIds.Count
      );

      var instanceDefinitionProxy = new InstanceDefinitionProxy
      {
        applicationId = definitionId,
        objects = conversionResult.ConvertedElementIds.ToList(), // Copy the list
        maxDepth = 0, // Linked models are at depth 0 for now (no nested instances)
        name = modelName
      };

      instanceDefinitionProxies.Add(instanceDefinitionProxy);
    }

    // Store the instance definition proxies on the root object
    if (instanceDefinitionProxies.Count > 0)
    {
      rootObject[ProxyKeys.INSTANCE_DEFINITION] = instanceDefinitionProxies;
      logger.LogInformation("Created {ProxyCount} InstanceDefinitionProxies", instanceDefinitionProxies.Count);
    }
  }

  // Helper method to generate a unique definition ID for a linked model
  private string GenerateDefinitionId(string documentPath)
  {
    // Create a consistent ID based on the document path
    // This ensures the same linked model always gets the same definition ID
    string fileName = Path.GetFileNameWithoutExtension(documentPath);
    string hash = ComputeSimpleHash(documentPath);
    return $"LinkedModel_{fileName}_{hash}";
  }

  // Simple hash function for generating consistent definition IDs
  private string ComputeSimpleHash(string input)
  {
    byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(input);

#pragma warning disable CA1850
    using (var sha256 = System.Security.Cryptography.SHA256.Create())
    {
      byte[] hashBytes = sha256.ComputeHash(inputBytes);
      // Take first 8 characters for a shorter but still unique hash
      return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant()[..8];
    }
#pragma warning restore CA1850
  }
}

public class LinkedModelConversionResult
{
  public string DocumentPath { get; set; }
  public List<DocumentToConvert> Instances { get; set; }
  public List<string> ConvertedElementIds { get; set; }

  public LinkedModelConversionResult(string documentPath, List<DocumentToConvert> instances)
  {
    DocumentPath = documentPath;
    Instances = instances;
    ConvertedElementIds = new List<string>();
  }
}
