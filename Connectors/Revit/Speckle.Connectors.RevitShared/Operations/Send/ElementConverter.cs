using Autodesk.Revit.DB;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Revit.HostApp;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Sdk;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.Revit.Operations.Send;

/// <summary>
/// Handles the core element conversion logic.
/// Focused purely on converting Revit elements to Speckle objects without proxy concerns.
/// </summary>
public class ElementConverter
{
  private readonly IRootToSpeckleConverter _converter;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly ElementUnpacker _elementUnpacker;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly SendCollectionManager _sendCollectionManager;
  private readonly LinkedModelDocumentHandler _linkedModelHandler;
  private readonly ILogger<ElementConverter> _logger;

  public ElementConverter(
    IRootToSpeckleConverter converter,
    ISendConversionCache sendConversionCache,
    ElementUnpacker elementUnpacker,
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    SendCollectionManager sendCollectionManager,
    LinkedModelDocumentHandler linkedModelHandler,
    ILogger<ElementConverter> logger
  )
  {
    _converter = converter;
    _sendConversionCache = sendConversionCache;
    _elementUnpacker = elementUnpacker;
    _converterSettings = converterSettings;
    _sendCollectionManager = sendCollectionManager;
    _linkedModelHandler = linkedModelHandler;
    _logger = logger;
  }

  /// <summary>
  /// Converts a main model document's elements.
  /// </summary>
  public MainModelConversionResult ConvertMainModel(DocumentToConvert mainModel, ConversionContext context)
  {
    if (mainModel == null)
    {
      return new MainModelConversionResult(new List<SendConversionResult>(), []);
    }

    var unpackedMainModel = UnpackDocument(mainModel);
    var conversionResults = new List<SendConversionResult>();
    int processedElements = 0;

    ConvertDocumentElements(
      unpackedMainModel,
      context,
      conversionResults,
      ref processedElements,
      mainModel.Elements.Count,
      trackingResult: null
    );

    return new MainModelConversionResult(conversionResults, unpackedMainModel.Elements);
  }

  /// <summary>
  /// Converts linked models for the instance proxy system.
  /// Each unique linked model is converted once without instance transforms.
  /// </summary>
  public LinkedModelConversionResults ConvertLinkedModelsForProxies(
    Dictionary<string, List<DocumentToConvert>> linkedModelGroups,
    ConversionContext context
  )
  {
    var linkedModelConversions = new List<LinkedModelConversionResult>();
    var allConversionResults = new List<SendConversionResult>();
    var allConvertedElements = new List<Element>();
    int totalProcessedElements = 0;

    var totalElementsToConvert = linkedModelGroups.Values.Sum(group => group.First().Elements.Count);

    foreach (var linkedModelGroup in linkedModelGroups)
    {
      var conversionResult = ConvertUniqueLinkedModel(
        linkedModelGroup,
        context,
        allConversionResults,
        ref totalProcessedElements,
        totalElementsToConvert
      );

      if (conversionResult != null)
      {
        linkedModelConversions.Add(conversionResult);
        // add the converted elements from the first instance (which was converted)
        var firstInstance = linkedModelGroup.Value.First();
        var unpackedModel = UnpackDocument(firstInstance with { Transform = null });
        allConvertedElements.AddRange(unpackedModel.Elements);
      }
    }

    return new LinkedModelConversionResults(linkedModelConversions, allConversionResults, allConvertedElements);
  }

  /// <summary>
  /// Converts a single unique linked model (first instance without transform).
  /// </summary>
  private LinkedModelConversionResult? ConvertUniqueLinkedModel(
    KeyValuePair<string, List<DocumentToConvert>> linkedModelGroup,
    ConversionContext context,
    List<SendConversionResult> allResults,
    ref int totalProcessedElements,
    int totalElementCount
  )
  {
    string documentPath = linkedModelGroup.Key;
    var instances = linkedModelGroup.Value;

    // create result tracking object
    var conversionResult = new LinkedModelConversionResult(documentPath, instances);

    // convert only the FIRST instance of each unique linked model (without its transform)
    var firstInstance = instances.First();
    var uniqueModelToConvert = firstInstance with { Transform = null }; // Remove transform
    var unpackedUniqueModel = UnpackDocument(uniqueModelToConvert);

    var modelResults = new List<SendConversionResult>();
    ConvertDocumentElements(
      unpackedUniqueModel,
      context,
      modelResults,
      ref totalProcessedElements,
      totalElementCount,
      conversionResult
    );

    allResults.AddRange(modelResults);

    return conversionResult.ConvertedElementIds.Count > 0 ? conversionResult : null;
  }

  /// <summary>
  /// Unpacks a document to get atomic elements ready for conversion.
  /// </summary>
  private DocumentToConvert UnpackDocument(DocumentToConvert documentToConvert)
  {
    using (_converterSettings.Push(currentSettings => currentSettings with { Document = documentToConvert.Doc }))
    {
      var atomicObjects = _elementUnpacker
        .UnpackSelectionForConversion(documentToConvert.Elements, documentToConvert.Doc)
        .ToList();
      return documentToConvert with { Elements = atomicObjects };
    }
  }

  /// <summary>
  /// Converts all elements in a document with proper tracking and progress reporting.
  /// </summary>
  private void ConvertDocumentElements(
    DocumentToConvert documentToConvert,
    ConversionContext context,
    List<SendConversionResult> results,
    ref int totalProcessedElements,
    int totalElementCount,
    LinkedModelConversionResult? trackingResult
  )
  {
    // get display name for linked models
    string? modelDisplayName = GetModelDisplayName(documentToConvert);

    // set conversion context - important: Transform is null for linked models in proxy system
    using (
      _converterSettings.Push(currentSettings =>
        currentSettings with
        {
          ReferencePointTransform = documentToConvert.Transform, // null for linked models
          Document = documentToConvert.Doc,
        }
      )
    )
    {
      foreach (Element revitElement in documentToConvert.Elements)
      {
        context.CancellationToken.ThrowIfCancellationRequested();

        var conversionResult = ConvertSingleElement(
          revitElement,
          documentToConvert,
          context,
          modelDisplayName,
          trackingResult
        );

        results.Add(conversionResult);

        // report progress
        ReportProgress(context.Progress, ++totalProcessedElements, totalElementCount);
      }
    }
  }

  /// <summary>
  /// Converts a single Revit element to a Speckle object.
  /// </summary>
  private SendConversionResult ConvertSingleElement(
    Element revitElement,
    DocumentToConvert documentToConvert,
    ConversionContext context,
    string? modelDisplayName,
    LinkedModelConversionResult? trackingResult
  )
  {
    string applicationId = revitElement.UniqueId;
    string sourceType = revitElement.GetType().Name;

    try
    {
      // Check if category is supported
      if (!SupportedCategoriesUtils.IsSupportedCategory(revitElement.Category))
      {
        var categoryName = revitElement.Category?.Name ?? "No category";
        return new SendConversionResult(
          Status.WARNING,
          revitElement.UniqueId,
          categoryName,
          null,
          new SpeckleException($"Category {categoryName} is not supported.")
        );
      }

      // Convert the element
      var converted = ConvertElementWithCaching(revitElement, documentToConvert, context.ProjectId, applicationId);

      // Add to appropriate collection
      var collection = _sendCollectionManager.GetAndCreateObjectHostCollection(
        revitElement,
        context.RootObject,
        context.SendWithLinkedModels,
        modelDisplayName
      );
      collection.elements.Add(converted);

      // Track converted element ID for proxy creation
      trackingResult?.ConvertedElementIds.Add(converted.applicationId ?? applicationId);

      return new SendConversionResult(Status.SUCCESS, applicationId, sourceType, converted);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      return new SendConversionResult(Status.ERROR, applicationId, sourceType, null, ex);
    }
  }

  /// <summary>
  /// Converts an element with caching support.
  /// </summary>
  private Base ConvertElementWithCaching(
    Element revitElement,
    DocumentToConvert documentToConvert,
    string projectId,
    string applicationId
  )
  {
    bool hasTransform = documentToConvert.Transform != null;

    // Try cache for non-transformed elements
    if (!hasTransform && _sendConversionCache.TryGetValue(projectId, applicationId, out ObjectReference? cachedValue))
    {
      return cachedValue;
    }

    // Convert the element
    var converted = _converter.Convert(revitElement);
    converted.applicationId = applicationId;

    return converted;
  }

  /// <summary>
  /// Gets the display name for a model (used for collection naming).
  /// </summary>
  private string? GetModelDisplayName(DocumentToConvert documentToConvert)
  {
    if (!documentToConvert.Doc.IsLinked)
    {
      return null;
    }

    string id = _linkedModelHandler.GetIdFromDocumentToConvert(documentToConvert);
    _linkedModelHandler.LinkedModelDisplayNames.TryGetValue(id, out string? modelDisplayName);
    return modelDisplayName;
  }

  /// <summary>
  /// Reports conversion progress.
  /// </summary>
  private static void ReportProgress(IProgress<CardProgress> progress, int processed, int total) =>
    progress.Report(new CardProgress("Converting", (double)processed / total));
}
