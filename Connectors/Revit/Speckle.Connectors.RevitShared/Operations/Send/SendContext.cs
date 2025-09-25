using Autodesk.Revit.DB;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Revit.HostApp;
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
    ConvertedElementIds = [];
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
  MainModelConversionResult? MainModelResult,
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
      var results = new List<SendConversionResult>();
      if (MainModelResult != null)
      {
        results.AddRange(MainModelResult.ConversionResults);
      }
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
      var elements = new List<Element>();
      if (MainModelResult != null)
      {
        elements.AddRange(MainModelResult.ConvertedElements);
      }
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
