using Autodesk.Revit.DB;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Utils;
using Speckle.Connectors.Revit.HostApp;
using Speckle.Connectors.Revit.Operations.Receive;
using Speckle.Connectors.Revit.Plugin;
using Speckle.Connectors.RevitShared;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Sdk;

namespace Speckle.Connectors.Revit.Bindings;

public static class ParameterScopes
{
  public const string INSTANCE = "Instance Parameters";
  public const string TYPE = "Type Parameters";
  public const string SYSTEM_TYPE = "System Type Parameters";
}

public record ParsedParameterPath(string Scope, string Category, string Name)
{
  public string[] ToArray() => [Scope, Category, Name];
}

internal sealed class RevitParametersBinding : IParametersBinding
{
  public string Name => "parametersBinding";
  public IBrowserBridge Parent { get; }

  private readonly RevitContext _revitContext;
  private readonly ITopLevelExceptionHandler _topLevelExceptionHandler;
  private readonly IRevitTask _revitTask;
  private readonly ParameterUpdater _parameterUpdater;
  private readonly IJsonSerializer _jsonSerializer;
  private readonly IBasicConnectorBinding _baseBinding;
  private readonly ILogger<RevitParametersBinding> _logger;

  public RevitParametersBinding(
    IBrowserBridge parent,
    RevitContext revitContext,
    ITopLevelExceptionHandler topLevelExceptionHandler,
    IRevitTask revitTask,
    ParameterUpdater parameterUpdater,
    IJsonSerializer jsonSerializer,
    IBasicConnectorBinding baseBinding,
    ILogger<RevitParametersBinding> logger
  )
  {
    Parent = parent;
    _revitContext = revitContext;
    _topLevelExceptionHandler = topLevelExceptionHandler;
    _revitTask = revitTask;
    _parameterUpdater = parameterUpdater;
    _jsonSerializer = jsonSerializer;
    _baseBinding = baseBinding;
    _logger = logger;
  }

  public async Task Update(string payload)
  {
    try
    {
      var wrapper = _jsonSerializer.Deserialize<ParameterChangesWrapper>(payload);
      var requests = wrapper?.Changes;

      if (requests == null || requests.Count == 0)
      {
        return;
      }

      var activeUIDoc =
        _revitContext.UIApplication?.ActiveUIDocument
        ?? throw new SpeckleException("Unable to retrieve active UI document");
      var doc = activeUIDoc.Document;

      int successCount = 0;
      List<string> errors = [];

      await _revitTask
        .RunAsync(() =>
        {
          using var t = new Transaction(doc, "Speckle: Apply Parameter Changes");

          // silence pop-ups like "duplicate mark values" etc. which blocks our param updates
          var failureOptions = t.GetFailureHandlingOptions();
          failureOptions.SetFailuresPreprocessor(new HideWarningsFailuresPreprocessor());
          t.SetFailureHandlingOptions(failureOptions);

          t.Start();

          foreach (var request in requests)
          {
            if (!TryValidateAndParseRequest(doc, request, out var element, out var parsedPath, out var errorMessage))
            {
              errors.Add(errorMessage!);
              continue;
            }

            object? rawValue = request.To;
            if (rawValue is Newtonsoft.Json.Linq.JValue jValue)
            {
              rawValue = jValue.Value;
            }

            var result = _parameterUpdater.Update(
              element!,
              parsedPath!.ToArray(),
              rawValue,
              request.InternalDefinitionName
            );

            if (result.IsSuccess)
            {
              successCount++;
            }
            else
            {
              errors.Add(result.ErrorMessage ?? "Unknown error");
            }
          }

          t.Commit();
        })
        .ConfigureAwait(false);

      if (errors.Count > 0)
      {
        var groupedErrors = errors.GroupBy(e => e).Select(g => $"{g.Count()} x {g.Key}");
        var errorString = string.Join(", ", groupedErrors);

        if (successCount > 0)
        {
          // Partial Success (Some worked, some failed)
          await _baseBinding.Commands.SetGlobalNotification(
            ToastNotificationType.WARNING,
            "Parameters updated with errors",
            $"Applied {successCount} updates. Encountered {errors.Count} errors: {errorString}",
            autoClose: false
          );
        }
        else
        {
          // Total Failure (None worked)
          await _baseBinding.Commands.SetGlobalNotification(
            ToastNotificationType.DANGER,
            "No parameters updated",
            $"All {errors.Count} updates failed: {errorString}",
            autoClose: false
          );
        }
      }
      else if (successCount > 0)
      {
        // Total Success
        await _baseBinding.Commands.SetGlobalNotification(
          ToastNotificationType.SUCCESS,
          "All parameters updated",
          $"Successfully applied {successCount} updates."
        );
      }
    }
    catch (Exception ex)
    {
      _topLevelExceptionHandler.CatchUnhandled(() =>
        throw new SpeckleException("Failed to apply parameter updates", ex)
      );
    }
  }

  private bool TryValidateAndParseRequest(
    Document doc,
    ParameterChangeRequest request,
    out Element? element,
    out ParsedParameterPath? parsedPath,
    out string? errorMessage
  )
  {
    element = null;
    parsedPath = null;
    errorMessage = null;

    if (string.IsNullOrEmpty(request.ApplicationId))
    {
      errorMessage = "Missing ApplicationId";
      return false;
    }

    if (ContainsLinkedModelTransformHash(request.ApplicationId))
    {
      errorMessage = "Cannot modify elements from a linked model";
      return false;
    }

    var elementId = ElementIdHelper.GetElementIdFromUniqueId(doc, request.ApplicationId);
    if (elementId == null)
    {
      errorMessage = "Element(s) not found in document";
      return false;
    }

    element = doc.GetElement(elementId);
    if (element == null)
    {
      errorMessage = "Element(s) not found in document";
      return false;
    }

    var rawPath = request.Path;
    if (string.IsNullOrEmpty(rawPath))
    {
      _logger.LogError("Widget / DUI payload error: parameter path missing");
      errorMessage = "Parameter path is missing";
      return false;
    }

    if (rawPath.StartsWith("properties.", StringComparison.InvariantCultureIgnoreCase))
    {
      rawPath = rawPath[11..];
    }

    if (rawPath.StartsWith("parameters.", StringComparison.InvariantCultureIgnoreCase))
    {
      rawPath = rawPath[11..];
    }

    var pathParts = rawPath.Split(['.'], 3);
    if (pathParts.Length != 3)
    {
      _logger.LogError(
        "Path format error: Expected exactly 3 parts (Scope.Category.Name) but received '{RawPath}' for element",
        rawPath
      );
      errorMessage = "Parameter path is incorrectly formatted";
      return false;
    }

    parsedPath = new ParsedParameterPath(pathParts[0], pathParts[1], pathParts[2]);
    return true;
  }

  private static bool ContainsLinkedModelTransformHash(string applicationId) =>
    // Evaluates if the ID contains the standard transform hash for linked elements
    System.Text.RegularExpressions.Regex.IsMatch(applicationId, @"_t[a-f0-9]+$");
}

public class ParameterChangesWrapper
{
  public List<ParameterChangeRequest>? Changes { get; set; }
}
