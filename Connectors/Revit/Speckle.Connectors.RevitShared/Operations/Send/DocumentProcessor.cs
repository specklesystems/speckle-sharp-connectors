using System.IO;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.DUI.Exceptions;
using Speckle.Connectors.Revit.HostApp;
using Speckle.Sdk;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.Revit.Operations.Send;

/// <summary>
/// Coordinates the processing of documents from grouping through conversion.
/// Acts as the orchestrator for document-level operations without getting into conversion details.
/// </summary>
public class DocumentProcessor
{
  private readonly LinkedModelDocumentHandler _linkedModelHandler;
  private readonly ElementConverter _elementConverter;
  private readonly DocumentValidator _validator;
  private readonly ILogger<DocumentProcessor> _logger;

  public DocumentProcessor(
    LinkedModelDocumentHandler linkedModelHandler,
    ElementConverter elementConverter,
    DocumentValidator validator,
    ILogger<DocumentProcessor> logger
  )
  {
    _linkedModelHandler = linkedModelHandler;
    _elementConverter = elementConverter;
    _validator = validator;
    _logger = logger;
  }

  /// <summary>
  /// Processes all documents from initial grouping through final conversion.
  /// This is the main entry point for document processing.
  /// </summary>
  public DocumentConversionResults ProcessDocuments(
    IReadOnlyList<DocumentToConvert> documentElementContexts,
    ConversionContext context
  )
  {
    // 1. Group and organize documents
    _logger.LogInformation("Processing {DocumentCount} documents", documentElementContexts.Count);
    var documentGroups = _linkedModelHandler.GroupAndPrepareDocuments(documentElementContexts);

    ValidateDocumentGroups(documentGroups);
    LogDocumentGroupInfo(documentGroups);

    // 2. Validate documents and filter invalid elements
    var validationResults = new List<SendConversionResult>();
    var validatedDocuments = _validator.FilterValidDocuments(
      documentGroups,
      context.SendWithLinkedModels,
      validationResults
    );

    ValidateProcessableDocuments(validatedDocuments);

    // 3. Convert main model
    var mainModelResult = _elementConverter.ConvertMainModel(validatedDocuments.MainModel.NotNull(), context);

    // 4. Convert linked models with proxy tracking
    LinkedModelConversionResults? linkedModelResults = null;
    if (context.SendWithLinkedModels && validatedDocuments.LinkedModelGroups.Count > 0)
    {
      linkedModelResults = _elementConverter.ConvertLinkedModelsForProxies(
        validatedDocuments.LinkedModelGroups,
        context
      );
    }

    // 5. Combine results
    var finalResults = CreateFinalResults(mainModelResult, linkedModelResults, validationResults);

    LogFinalStats(finalResults);
    ValidateFinalResults(finalResults);

    return finalResults;
  }

  /// <summary>
  /// Validates that we have at least a main model to process.
  /// </summary>
  private void ValidateDocumentGroups(DocumentGroups documentGroups)
  {
    if (documentGroups.MainModel == null)
    {
      throw new SpeckleException("Main Model not found.");
    }
  }

  /// <summary>
  /// Logs information about the grouped documents.
  /// </summary>
  private void LogDocumentGroupInfo(DocumentGroups documentGroups)
  {
    _logger.LogInformation(
      "Found {MainModelCount} main model and {LinkedModelCount} unique linked model(s)",
      documentGroups.MainModel != null ? 1 : 0,
      documentGroups.LinkedModelGroups.Count
    );

    foreach (var linkedModelGroup in documentGroups.LinkedModelGroups)
    {
      string documentPath = linkedModelGroup.Key;
      var instances = linkedModelGroup.Value;

      _logger.LogInformation(
        "Linked model '{DocumentPath}' has {InstanceCount} instance(s)",
        Path.GetFileName(documentPath),
        instances.Count
      );
    }
  }

  /// <summary>
  /// Validates that we have processable documents after validation.
  /// </summary>
  private void ValidateProcessableDocuments(ValidatedDocuments validatedDocuments)
  {
    bool hasMainModel = validatedDocuments.MainModel?.Elements.Count > 0;
    bool hasLinkedModels = validatedDocuments.LinkedModelGroups.Count > 0;

    if (!hasMainModel && !hasLinkedModels)
    {
      throw new SpeckleSendFilterException("No objects were found. Please update your publish filter!");
    }
  }

  /// <summary>
  /// Combines all conversion results into a single result object.
  /// </summary>
  private DocumentConversionResults CreateFinalResults(
    MainModelConversionResult mainModelResult,
    LinkedModelConversionResults? linkedModelResults,
    List<SendConversionResult> validationResults
  )
  {
    // Add validation results to main model results
    var combinedMainResults = new List<SendConversionResult>(mainModelResult.ConversionResults);
    combinedMainResults.AddRange(validationResults);

    var updatedMainModelResult = new MainModelConversionResult(combinedMainResults, mainModelResult.ConvertedElements);

    return new DocumentConversionResults(updatedMainModelResult, linkedModelResults);
  }

  /// <summary>
  /// Logs final conversion statistics.
  /// </summary>
  private void LogFinalStats(DocumentConversionResults results)
  {
    var totalResults = results.AllResults;
    var successCount = totalResults.Count(r => r.Status == Status.SUCCESS);
    var warningCount = totalResults.Count(r => r.Status == Status.WARNING);
    var errorCount = totalResults.Count(r => r.Status == Status.ERROR);

    _logger.LogInformation(
      "Conversion completed: {Success} success, {Warning} warnings, {Error} errors",
      successCount,
      warningCount,
      errorCount
    );

    if (results.LinkedModelResults != null)
    {
      var linkedModelCount = results.LinkedModelResults.LinkedModelConversions.Count;
      var totalInstances = results.LinkedModelResults.LinkedModelConversions.Sum(lm => lm.Instances.Count);

      if (totalInstances > linkedModelCount)
      {
        var efficiencyGain = (1.0 - (double)linkedModelCount / totalInstances) * 100;
        _logger.LogInformation(
          "Instance proxy efficiency: {EfficiencyGain:F1}% reduction ({TotalInstances} → {UniqueModels})",
          efficiencyGain,
          totalInstances,
          linkedModelCount
        );
      }
    }
  }

  /// <summary>
  /// Validates that the conversion didn't completely fail.
  /// </summary>
  private void ValidateFinalResults(DocumentConversionResults results)
  {
    if (results.AllFailed)
    {
      throw new SpeckleException("Failed to convert all objects.");
    }

    var skippedCount = results.AllResults.Count(r => r.Status == Status.WARNING);
    var totalCount = results.AllResults.Count;

    if (skippedCount == totalCount)
    {
      throw new SpeckleException("No supported objects visible. Update publish filter or check publish settings.");
    }
  }
}
