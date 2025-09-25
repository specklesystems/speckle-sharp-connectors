using Autodesk.Revit.DB;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.DUI.Exceptions;
using Speckle.Connectors.Revit.HostApp;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Sdk;

namespace Speckle.Connectors.Revit.Operations.Send;

/// <summary>
/// Unified processor for all document types (main and linked models).
/// </summary>
public class DocumentProcessor
{
  private readonly IRootToSpeckleConverter _converter;
  private readonly ElementUnpacker _elementUnpacker;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly SendCollectionManager _sendCollectionManager;

  public DocumentProcessor(
    IRootToSpeckleConverter converter,
    ElementUnpacker elementUnpacker,
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    SendCollectionManager sendCollectionManager
  )
  {
    _converter = converter;
    _elementUnpacker = elementUnpacker;
    _converterSettings = converterSettings;
    _sendCollectionManager = sendCollectionManager;
  }

  /// <summary>
  /// Main entry point: processes all documents from grouping through conversion.
  /// </summary>
  public DocumentConversionResults ProcessDocuments(
    IReadOnlyList<DocumentToConvert> documents,
    ConversionContext context
  )
  {
    // Step 1: Group and validate documents in one pass
    var processed = GroupAndValidateDocuments(documents, context);

    if (!processed.HasProcessableContent)
    {
      throw new SpeckleSendFilterException("No objects were found. Please update your publish filter!");
    }

    // Step 2: Convert main model if present
    MainModelConversionResult? mainResult = null;
    if (processed.MainModel != null)
    {
      mainResult = ConvertMainModel(processed.MainModel, context);
    }

    // Step 3: Convert linked models if enabled and present
    LinkedModelConversionResults? linkedResults = null;
    if (context.SendWithLinkedModels && processed.LinkedModelGroups.Count != 0)
    {
      linkedResults = ConvertLinkedModels(processed.LinkedModelGroups, context);
    }

    // Step 4: Create final results
    var finalResults = new DocumentConversionResults(mainResult, linkedResults);
    ValidateResults(finalResults);

    return finalResults;
  }

  /// <summary>
  /// Groups documents by type and validates them in a single pass.
  /// </summary>
  private ProcessedDocuments GroupAndValidateDocuments(
    IReadOnlyList<DocumentToConvert> documents,
    ConversionContext context
  )
  {
    var result = new ProcessedDocuments();

    foreach (var doc in documents)
    {
      if (doc?.Doc == null)
      {
        continue;
      }

      // filter out invalid elements early
      var validElements = doc.Elements.Where(e => e?.Category != null).ToList();
      if (validElements.Count == 0)
      {
        continue;
      }

      var validDoc = doc with { Elements = validElements };

      if (doc.Doc.IsLinked)
      {
        ProcessLinkedModel(validDoc, context, result);
      }
      else
      {
        result = new ProcessedDocuments
        {
          MainModel = validDoc,
          LinkedModelGroups = result.LinkedModelGroups,
          ValidationResults = result.ValidationResults
        };
      }
    }

    return result;
  }

  private void ProcessLinkedModel(DocumentToConvert linkedDoc, ConversionContext context, ProcessedDocuments result)
  {
    if (!context.SendWithLinkedModels)
    {
      var warning = new SendConversionResult(
        Status.WARNING,
        linkedDoc.Doc.PathName,
        typeof(RevitLinkInstance).ToString(),
        null,
        new SpeckleException("Enable linked model support from the settings to send this object")
      );

      result.ValidationResults.Add(warning);
      return;
    }

    // group by document path (same file, different positions)
    var documentPath = linkedDoc.Doc.PathName;
    if (!result.LinkedModelGroups.TryGetValue(documentPath, out var group))
    {
      group = [];
      result.LinkedModelGroups[documentPath] = group;
    }

    group.Add(linkedDoc);
  }

  /// <summary>
  /// Converts main model
  /// </summary>
  private MainModelConversionResult ConvertMainModel(DocumentToConvert mainModel, ConversionContext context)
  {
    var unpackedModel = UnpackDocument(mainModel);
    var results = new List<SendConversionResult>();
    int totalElements = mainModel.Elements.Count;
    int processedElements = 0;

    foreach (var element in unpackedModel.Elements)
    {
      context.CancellationToken.ThrowIfCancellationRequested();

      var result = ConvertElement(element, context, null);
      results.Add(result);

      context.Progress.Report(new CardProgress("Converting", ++processedElements / (double)totalElements));
    }

    return new MainModelConversionResult(results, unpackedModel.Elements);
  }

  /// <summary>
  /// Converts linked models with proxy tracking
  /// </summary>
  private LinkedModelConversionResults ConvertLinkedModels(
    Dictionary<string, List<DocumentToConvert>> linkedGroups,
    ConversionContext context
  )
  {
    var conversions = new List<LinkedModelConversionResult>();
    var allConversionResults = new List<SendConversionResult>();
    var allConvertedElements = new List<Element>();

    foreach (var kvp in linkedGroups)
    {
      var documentPath = kvp.Key;
      var instances = kvp.Value;

      // convert the first instance (unique model) without transform
      var firstInstance = instances.First();
      var modelWithoutTransform = firstInstance with { Transform = null };

      var unpackedModel = UnpackDocument(modelWithoutTransform);
      var trackingResult = new LinkedModelConversionResult(documentPath, instances);

      foreach (var element in unpackedModel.Elements)
      {
        context.CancellationToken.ThrowIfCancellationRequested();
        var result = ConvertElement(element, context, trackingResult);
        allConversionResults.Add(result);
      }

      conversions.Add(trackingResult);
      allConvertedElements.AddRange(unpackedModel.Elements);
    }

    return new LinkedModelConversionResults(conversions, allConversionResults, allConvertedElements);
  }

  private SendConversionResult ConvertElement(
    Element element,
    ConversionContext context,
    LinkedModelConversionResult? trackingResult
  )
  {
    string applicationId = element.UniqueId;
    string sourceType = element.GetType().Name;

    try
    {
      using (_converterSettings.Push(settings => settings with { Document = element.Document }))
      {
        var converted = _converter.Convert(element);
        converted.applicationId = applicationId;

        // add to appropriate collection
        var collection = _sendCollectionManager.GetAndCreateObjectHostCollection(
          element,
          context.RootObject,
          context.SendWithLinkedModels,
          null // TODO: add model display name logic later if needed
        );
        collection.elements.Add(converted);

        // add to tracking result if provided
        trackingResult?.ConvertedElementIds.Add(converted.applicationId ?? applicationId);

        return new SendConversionResult(Status.SUCCESS, applicationId, sourceType, converted);
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      return new SendConversionResult(Status.ERROR, applicationId, sourceType, null, ex);
    }
  }

  /// <summary>
  /// Unpacks document elements
  /// </summary>
  private DocumentToConvert UnpackDocument(DocumentToConvert document)
  {
    using (_converterSettings.Push(settings => settings with { Document = document.Doc }))
    {
      var atomicObjects = _elementUnpacker.UnpackSelectionForConversion(document.Elements, document.Doc).ToList();

      return document with
      {
        Elements = atomicObjects
      };
    }
  }

  /// <summary>
  /// Simple validation of final results.
  /// </summary>
  private static void ValidateResults(DocumentConversionResults results)
  {
    if (results.AllFailed)
    {
      throw new SpeckleException("Failed to convert all objects");
    }

    var totalResults = results.AllResults.Count;
    var skippedCount = results.AllResults.Count(r => r.Status == Status.WARNING);

    if (skippedCount == totalResults)
    {
      throw new SpeckleException("No supported objects visible. Update publish filter or check publish settings");
    }
  }
}
