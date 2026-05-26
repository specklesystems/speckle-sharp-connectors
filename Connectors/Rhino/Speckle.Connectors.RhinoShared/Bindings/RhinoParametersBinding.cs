using Microsoft.Extensions.Logging;
using Rhino;
using Rhino.DocObjects;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Utils;
using Speckle.Connectors.Rhino.HostApp;
using Speckle.Sdk;

namespace Speckle.Connectors.Rhino.Bindings;

internal sealed class RhinoParametersBinding : IParametersBinding
{
  public string Name => "parametersBinding";
  public IBrowserBridge Parent { get; }

  private readonly ITopLevelExceptionHandler _topLevelExceptionHandler;
  private readonly RhinoPropertyUpdater _propertyUpdater;
  private readonly IJsonSerializer _jsonSerializer;
  private readonly IBasicConnectorBinding _baseBinding;
  private readonly ILogger<RhinoParametersBinding> _logger;

  public RhinoParametersBinding(
    IBrowserBridge parent,
    ITopLevelExceptionHandler topLevelExceptionHandler,
    RhinoPropertyUpdater propertyUpdater,
    IJsonSerializer jsonSerializer,
    IBasicConnectorBinding baseBinding,
    ILogger<RhinoParametersBinding> logger
  )
  {
    Parent = parent;
    _topLevelExceptionHandler = topLevelExceptionHandler;
    _propertyUpdater = propertyUpdater;
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

      if (requests is null || requests.Count == 0)
      {
        return;
      }

      var doc = RhinoDoc.ActiveDoc ?? throw new SpeckleException("Unable to retrieve active Rhino document");

      int successCount = 0;
      List<string> errors = [];

      uint undoRecord = doc.BeginUndoRecord("Speckle: Apply Parameter Changes");
      try
      {
        foreach (var request in requests)
        {
          if (!TryValidateAndParseRequest(doc, request, out var rhinoObject, out var pathParts, out var errorMessage))
          {
            errors.Add(errorMessage!);
            continue;
          }

          object? rawValue = request.To is Newtonsoft.Json.Linq.JValue jValue ? jValue.Value : request.To;

          var result = _propertyUpdater.Update(rhinoObject!, pathParts!, rawValue);
          if (result.IsSuccess)
          {
            successCount++;
          }
          else
          {
            errors.Add(result.ErrorMessage ?? "Unknown error");
          }
        }
      }
      finally
      {
        doc.EndUndoRecord(undoRecord);
      }

      if (errors.Count > 0)
      {
        var groupedErrors = errors.GroupBy(e => e).Select(g => $"{g.Count()} x {g.Key}");
        var errorString = string.Join(", ", groupedErrors);

        if (successCount > 0)
        {
          await _baseBinding.Commands.SetGlobalNotification(
            ToastNotificationType.WARNING,
            "Parameters updated with errors",
            $"Applied {successCount} updates. Encountered {errors.Count} errors: {errorString}",
            autoClose: false
          );
        }
        else
        {
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
        await _baseBinding.Commands.SetGlobalNotification(
          ToastNotificationType.SUCCESS,
          "All parameters updated",
          $"Successfully applied {successCount} updates."
        );
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _topLevelExceptionHandler.CatchUnhandled(() =>
        throw new SpeckleException("Failed to apply parameter updates", ex)
      );
    }
  }

  private bool TryValidateAndParseRequest(
    RhinoDoc doc,
    ParameterChangeRequest request,
    out RhinoObject? rhinoObject,
    out string[]? pathParts,
    out string? errorMessage
  )
  {
    rhinoObject = null;
    pathParts = null;
    errorMessage = null;

    if (string.IsNullOrEmpty(request.ApplicationId))
    {
      errorMessage = "Missing ApplicationId";
      return false;
    }

    if (!Guid.TryParse(request.ApplicationId, out var objectId))
    {
      errorMessage = $"ApplicationId is not a valid GUID: {request.ApplicationId}";
      return false;
    }

    rhinoObject = doc.Objects.FindId(objectId);
    if (rhinoObject is null)
    {
      errorMessage = $"Object not found: {request.ApplicationId}";
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
    else if (rawPath.StartsWith("parameters.", StringComparison.InvariantCultureIgnoreCase))
    {
      rawPath = rawPath[11..];
    }

    if (string.IsNullOrEmpty(rawPath))
    {
      errorMessage = "Parameter path is empty after prefix stripping";
      return false;
    }

    pathParts = rawPath.Split('.');
    return true;
  }
}
