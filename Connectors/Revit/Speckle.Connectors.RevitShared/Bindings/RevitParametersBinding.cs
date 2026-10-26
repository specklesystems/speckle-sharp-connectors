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
  private readonly RevitParameterCreator _parameterCreator;
  private readonly IJsonSerializer _jsonSerializer;
  private readonly IBasicConnectorBinding _baseBinding;
  private readonly ILogger<RevitParametersBinding> _logger;

  public RevitParametersBinding(
    IBrowserBridge parent,
    RevitContext revitContext,
    ITopLevelExceptionHandler topLevelExceptionHandler,
    IRevitTask revitTask,
    ParameterUpdater parameterUpdater,
    RevitParameterCreator parameterCreator,
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
    _parameterCreator = parameterCreator;
    _jsonSerializer = jsonSerializer;
    _baseBinding = baseBinding;
    _logger = logger;
  }

  public async Task<ParameterUpdateSummary> Update(string payload)
  {
    int successCount = 0;
    List<string> errors = [];
    try
    {
      var wrapper = _jsonSerializer.Deserialize<ParameterChangesWrapper>(payload);
      var requests = wrapper?.Changes;

      if (requests == null || requests.Count == 0)
      {
        return new ParameterUpdateSummary(0, 0, []);
      }

      var activeUIDoc =
        _revitContext.UIApplication?.ActiveUIDocument
        ?? throw new SpeckleException("Unable to retrieve active UI document");
      var doc = activeUIDoc.Document;

      await _revitTask
        .RunAsync(() =>
        {
          using var t = new Transaction(doc, "Speckle: Apply Parameter Changes");

          // silence pop-ups like "duplicate mark values" etc. which blocks our param updates
          var failureOptions = t.GetFailureHandlingOptions();
          failureOptions.SetFailuresPreprocessor(new HideWarningsFailuresPreprocessor());
          t.SetFailureHandlingOptions(failureOptions);

          t.Start();

          void Record(UpdateResult result)
          {
            if (result.IsSuccess)
            {
              successCount++;
            }
            else
            {
              errors.Add(result.ErrorMessage ?? "Unknown error");
            }
          }

          foreach (var request in requests)
          {
            if (!TryResolveElement(doc, request, out var element, out var elementError))
            {
              errors.Add(elementError!);
              continue;
            }

            object? rawValue = request.To is Newtonsoft.Json.Linq.JValue jValue ? jValue.Value : request.To;

            // a creation flag only says the widget saw no value for this object; when the path resolves to a
            // parameter the element does have it, so it is updated rather than shadowed by a new one
            var hasParameterPath = TryParsePath(request.Path, out var parsedPath, out var pathError);
            if (
              hasParameterPath
              && (
                !request.IsCreation
                || _parameterUpdater.Exists(element!, parsedPath!.ToArray(), request.InternalDefinitionName)
              )
            )
            {
              Record(
                _parameterUpdater.Update(element!, parsedPath!.ToArray(), rawValue, request.InternalDefinitionName)
              );
              continue;
            }

            if (!request.IsCreation)
            {
              errors.Add(pathError!);
              continue;
            }

            var paramName = ExtractCreationParamName(request.Path);
            if (string.IsNullOrEmpty(paramName))
            {
              errors.Add($"Invalid path for new parameter: '{request.Path}'");
              continue;
            }

            Record(_parameterCreator.CreateAndSet(doc, element!, paramName, rawValue));
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

      return new ParameterUpdateSummary(successCount, errors.Count, errors);
    }
    catch (Exception ex)
    {
      _topLevelExceptionHandler.CatchUnhandled(() =>
        throw new SpeckleException("Failed to apply parameter updates", ex)
      );
      errors.Add(ex.Message);
      return new ParameterUpdateSummary(successCount, errors.Count, errors);
    }
  }

  private static bool TryResolveElement(
    Document doc,
    ParameterChangeRequest request,
    out Element? element,
    out string? errorMessage
  )
  {
    element = null;
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
    element = elementId is not null ? doc.GetElement(elementId) : null;
    if (element == null)
    {
      errorMessage = "Element(s) not found in document";
      return false;
    }

    return true;
  }

  private bool TryParsePath(string? path, out ParsedParameterPath? parsedPath, out string? errorMessage)
  {
    parsedPath = null;
    errorMessage = null;

    if (string.IsNullOrEmpty(path))
    {
      _logger.LogError("Widget / DUI payload error: parameter path missing");
      errorMessage = "Parameter path is missing";
      return false;
    }

    var rawPath = StripPrefixes(path!);
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

  private static string StripPrefixes(string path)
  {
    if (path.StartsWith("properties.", StringComparison.OrdinalIgnoreCase))
    {
      path = path[11..];
    }

    if (path.StartsWith("parameters.", StringComparison.OrdinalIgnoreCase))
    {
      path = path[11..];
    }

    return path;
  }

  private static bool ContainsLinkedModelTransformHash(string applicationId) =>
    // Evaluates if the ID contains the standard transform hash for linked elements
    System.Text.RegularExpressions.Regex.IsMatch(applicationId, @"_t[a-f0-9]+$");

  /// <summary>
  /// The name a created parameter gets: the bare name behind the widget's "properties." / "parameters." prefix
  /// (e.g. "properties.SpeckleTag" → "SpeckleTag"), or the leaf of a full scope.group.name path, so a creation
  /// request on an existing path is never named after the whole path.
  /// </summary>
  private static string ExtractCreationParamName(string path)
  {
    var name = StripPrefixes(path).Trim();
    var parts = name.Split(['.'], 3);
    if (
      parts.Length == 3
      && parts[0] is ParameterScopes.INSTANCE or ParameterScopes.TYPE or ParameterScopes.SYSTEM_TYPE
    )
    {
      return parts[2].Trim();
    }

    return name;
  }
}
