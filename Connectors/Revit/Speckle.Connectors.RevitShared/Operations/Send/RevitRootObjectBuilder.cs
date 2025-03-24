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
  IThreadContext threadContext,
  SendCollectionManager sendCollectionManager,
  ILogger<RevitRootObjectBuilder> logger,
  RevitToSpeckleCacheSingleton revitToSpeckleCacheSingleton
) : IRootObjectBuilder<DocumentToConvert>
{
  // Dictionary to track linked model display names
  private readonly Dictionary<string, string> _linkedModelDisplayNames = new();

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
      PrepareLinkedModelNames(documentElementContexts);
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
        string id =
          atomicObjectByDocumentAndTransform.Doc.GetHashCode()
          + "-"
          + (atomicObjectByDocumentAndTransform.Transform?.GetHashCode() ?? 0);
        _linkedModelDisplayNames.TryGetValue(id, out modelDisplayName);
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
            if (
              !hasTransform
              && sendConversionCache.TryGetValue(sendInfo.ProjectId, applicationId, out ObjectReference? value)
            )
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
                string transformHash = GetTransformHash(atomicObjectByDocumentAndTransform.Transform.NotNull());
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

  // Helper method to generate a simple hash for a transform
  // transformedElement.applicationId = ${applicationId}-t{transformHash}
  private string GetTransformHash(Transform transform)
  {
    // create a simplified representation of the transform
    string json =
      $@"{{
      ""origin"": [{transform.Origin.X:F2}, {transform.Origin.Y:F2}, {transform.Origin.Z:F2}],
      ""basis"": [{transform.BasisX.X:F1}, {transform.BasisY.Y:F1}, {transform.BasisZ.Z:F1}]
    }}";

    byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);

#pragma warning disable CA1850
    using (var sha256 = System.Security.Cryptography.SHA256.Create())
    {
      byte[] hashBytes = sha256.ComputeHash(jsonBytes);
      // keep only the first 8 characters for a short but unique hash
      return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant()[..8];
    }
#pragma warning restore CA1850
  }

  /// <summary>
  /// Prepares display names for linked model documents based on filename
  /// </summary>
  private void PrepareLinkedModelNames(IReadOnlyList<DocumentToConvert> documentElementContexts)
  {
    _linkedModelDisplayNames.Clear();

    // Group linked models by filename
    var linkedModels = documentElementContexts
      .Where(ctx => ctx.Doc.IsLinked)
      .GroupBy(ctx => Path.GetFileNameWithoutExtension(ctx.Doc.PathName))
      .ToDictionary(g => g.Key, g => g.ToList());

    // Create a unique key for each instance
    foreach (var group in linkedModels)
    {
      string baseName = group.Key;
      var instances = group.Value;

      // Single instance - just use the base name
      if (instances.Count == 1)
      {
        string id = instances[0].Doc.GetHashCode() + "-" + (instances[0].Transform?.GetHashCode() ?? 0);
        _linkedModelDisplayNames[id] = baseName;
      }
      // Multiple instances - add numbering
      else
      {
        for (int i = 0; i < instances.Count; i++)
        {
          string id = instances[i].Doc.GetHashCode() + "-" + (instances[i].Transform?.GetHashCode() ?? 0);
          _linkedModelDisplayNames[id] = $"{baseName}_{i + 1}";
        }
      }
    }
  }
}
