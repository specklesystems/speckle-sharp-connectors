using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.DUI.Exceptions;
using Speckle.Connectors.Revit.HostApp;
using Speckle.Sdk;

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

  public DocumentProcessor(
    LinkedModelDocumentHandler linkedModelHandler,
    ElementConverter elementConverter,
    DocumentValidator validator
  )
  {
    _linkedModelHandler = linkedModelHandler;
    _elementConverter = elementConverter;
    _validator = validator;
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
    // group and organize documents
    var documentGroups = _linkedModelHandler.GroupAndPrepareDocuments(documentElementContexts);

    // validate documents and filter invalid elements
    var validationResults = new List<SendConversionResult>();
    var validatedDocuments = _validator.FilterValidDocuments(
      documentGroups,
      context.SendWithLinkedModels,
      validationResults
    );
    ValidateProcessableDocuments(validatedDocuments);

    // convert main model
    MainModelConversionResult? mainModelResult = null;
    if (validatedDocuments.MainModel != null)
    {
      mainModelResult = _elementConverter.ConvertMainModel(validatedDocuments.MainModel, context);
    }

    // convert linked models with proxy tracking
    LinkedModelConversionResults? linkedModelResults = null;
    if (context.SendWithLinkedModels && validatedDocuments.LinkedModelGroups.Count > 0)
    {
      linkedModelResults = _elementConverter.ConvertLinkedModelsForProxies(
        validatedDocuments.LinkedModelGroups,
        context
      );
    }

    // combine results
    var finalResults = CreateFinalResults(mainModelResult, linkedModelResults);
    ValidateFinalResults(finalResults);

    return finalResults;
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
    MainModelConversionResult? mainModelResult,
    LinkedModelConversionResults? linkedModelResults
  ) => new(mainModelResult, linkedModelResults);

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
