using Autodesk.Revit.DB;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Revit.HostApp;
using Speckle.Sdk;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.Revit.Operations.Send;

/// <summary>
/// Context object that contains all the parameters needed for conversion operations.
/// Reduces parameter passing and makes method signatures cleaner.
/// </summary>
public record ConversionContext(
  string ProjectId,
  Collection RootObject,
  bool SendWithLinkedModels,
  IProgress<CardProgress> Progress,
  CancellationToken CancellationToken
);

/// <summary>
/// Tracks the conversion results for a single linked model.
/// Used to coordinate between element conversion and proxy creation.
/// </summary>
public class LinkedModelConversionResult
{
  public string DocumentPath { get; }
  public List<DocumentToConvert> Instances { get; }
  public List<string> ConvertedElementIds { get; }

  public LinkedModelConversionResult(string documentPath, List<DocumentToConvert> instances)
  {
    DocumentPath = documentPath;
    Instances = instances;
    ConvertedElementIds = new List<string>();
  }
}

/// <summary>
/// Contains the results of converting linked models with proxy tracking.
/// </summary>
public record LinkedModelConversionResults(
  List<LinkedModelConversionResult> LinkedModelConversions,
  List<SendConversionResult> ConversionResults,
  List<Element> ConvertedElements
);

/// <summary>
/// Contains the results of converting the main model.
/// </summary>
public record MainModelConversionResult(List<SendConversionResult> ConversionResults, List<Element> ConvertedElements);

/// <summary>
/// Aggregates all conversion results from main model and linked models.
/// </summary>
public record DocumentConversionResults(
  MainModelConversionResult MainModelResult,
  LinkedModelConversionResults? LinkedModelResults
)
{
  /// <summary>
  /// All conversion results combined from main and linked models.
  /// </summary>
  public List<SendConversionResult> AllResults
  {
    get
    {
      var results = new List<SendConversionResult>(MainModelResult.ConversionResults);
      if (LinkedModelResults != null)
      {
        results.AddRange(LinkedModelResults.ConversionResults);
      }
      return results;
    }
  }

  /// <summary>
  /// All converted elements from main and linked models.
  /// </summary>
  public List<Element> AllElements
  {
    get
    {
      var elements = new List<Element>(MainModelResult.ConvertedElements);
      if (LinkedModelResults != null)
      {
        elements.AddRange(LinkedModelResults.ConvertedElements);
      }
      return elements;
    }
  }

  /// <summary>
  /// Check if any conversions resulted in errors.
  /// </summary>
  public bool HasErrors => AllResults.Any(r => r.Status == Status.ERROR);

  /// <summary>
  /// Check if all conversions failed.
  /// </summary>
  public bool AllFailed => AllResults.All(r => r.Status == Status.ERROR);
}

/// <summary>
/// Validates documents and filters out invalid elements.
/// Separated from conversion logic for better testability.
/// </summary>
public class DocumentValidator
{
  /// <summary>
  /// Filters and validates documents, removing invalid elements and adding appropriate warnings.
  /// </summary>
  public ValidatedDocuments FilterValidDocuments(
    DocumentGroups documentGroups,
    bool sendWithLinkedModels,
    List<SendConversionResult> results
  )
  {
    var validMainModel = ValidateMainModel(documentGroups.MainModel);
    var validLinkedModelGroups = ValidateLinkedModelGroups(
      documentGroups.LinkedModelGroups,
      sendWithLinkedModels,
      results
    );

    return new ValidatedDocuments(validMainModel, validLinkedModelGroups);
  }

  private DocumentToConvert? ValidateMainModel(DocumentToConvert? mainModel)
  {
    if (mainModel == null)
    {
      return null;
    }

    var validElements = FilterValidElements(mainModel.Elements);
    return validElements.Count > 0 ? mainModel with { Elements = validElements } : null;
  }

  private Dictionary<string, List<DocumentToConvert>> ValidateLinkedModelGroups(
    Dictionary<string, List<DocumentToConvert>> linkedModelGroups,
    bool sendWithLinkedModels,
    List<SendConversionResult> results
  )
  {
    var validLinkedModelGroups = new Dictionary<string, List<DocumentToConvert>>();

    foreach (var linkedModelGroup in linkedModelGroups)
    {
      if (!sendWithLinkedModels)
      {
        AddLinkedModelWarnings(linkedModelGroup.Value, results);
        continue;
      }

      var validatedGroup = ValidateLinkedModelGroup(linkedModelGroup);
      if (validatedGroup != null)
      {
        validLinkedModelGroups[linkedModelGroup.Key] = validatedGroup;
      }
    }

    return validLinkedModelGroups;
  }

  private void AddLinkedModelWarnings(List<DocumentToConvert> instances, List<SendConversionResult> results)
  {
    foreach (var instance in instances)
    {
      results.Add(
        new SendConversionResult(
          Status.WARNING,
          instance.Doc.PathName,
          typeof(RevitLinkInstance).ToString(),
          null,
          new SpeckleException("Enable linked model support from the settings to send this object")
        )
      );
    }
  }

  private List<DocumentToConvert>? ValidateLinkedModelGroup(
    KeyValuePair<string, List<DocumentToConvert>> linkedModelGroup
  )
  {
    // For linked models, we only need to validate the first instance since we convert unique models once
    var firstInstance = linkedModelGroup.Value.First();
    var validElements = FilterValidElements(firstInstance.Elements);

    if (validElements.Count == 0)
    {
      return null;
    }

    // Keep all instances but update the first one with valid elements
    var validInstances = linkedModelGroup.Value.ToList();
    validInstances[0] = validInstances[0] with { Elements = validElements };
    return validInstances;
  }

  private List<Element> FilterValidElements(List<Element> elements) =>
    elements.Where(element => element?.Category != null).ToList();
}

/// <summary>
/// Contains validated documents ready for conversion.
/// </summary>
public record ValidatedDocuments(
  DocumentToConvert? MainModel,
  Dictionary<string, List<DocumentToConvert>> LinkedModelGroups
);
