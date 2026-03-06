using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
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

internal sealed class RevitParametersBinding : IParametersBinding
{
  public string Name { get; } = "parametersBinding";
  public IBrowserBridge Parent { get; }

  private readonly RevitContext _revitContext;
  private readonly ITopLevelExceptionHandler _topLevelExceptionHandler;
  private readonly IRevitTask _revitTask;
  private readonly ParameterUpdater _parameterUpdater;
  private readonly IJsonSerializer _jsonSerializer;
  private readonly IBasicConnectorBinding _baseBinding;

  public RevitParametersBinding(
    IBrowserBridge parent,
    RevitContext revitContext,
    ITopLevelExceptionHandler topLevelExceptionHandler,
    IRevitTask revitTask,
    ParameterUpdater parameterUpdater,
    IJsonSerializer jsonSerializer,
    IBasicConnectorBinding baseBinding
  )
  {
    Parent = parent;
    _revitContext = revitContext;
    _topLevelExceptionHandler = topLevelExceptionHandler;
    _revitTask = revitTask;
    _parameterUpdater = parameterUpdater;
    _jsonSerializer = jsonSerializer;
    _baseBinding = baseBinding;
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
            if (string.IsNullOrEmpty(request.ApplicationId))
            {
              errors.Add("Missing ApplicationId.");
              continue;
            }

            var elementId = ElementIdHelper.GetElementIdFromUniqueId(doc, request.ApplicationId);
            if (elementId == null)
            {
              errors.Add($"Element UniqueId not found: {request.ApplicationId}");
              continue;
            }

            var element = doc.GetElement(elementId);
            if (element == null)
            {
              errors.Add($"Element is null for Id: {elementId}");
              continue;
            }

            var rawPath = request.Path;
            if (string.IsNullOrEmpty(rawPath))
            {
              errors.Add("Path is missing.");
              continue;
            }

            // TODO: not happy about this
            // 👇
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
              errors.Add($"Path must have 3 parts. Got: '{rawPath}'");
              continue;
            }
            // ☝️
            // TODO: not happy about this

            object? rawValue = request.To;
            if (rawValue is Newtonsoft.Json.Linq.JValue jValue)
            {
              rawValue = jValue.Value;
            }

            var result = _parameterUpdater.Update(element, pathParts, rawValue);

            if (result.IsSuccess)
            {
              successCount++;
            }
            else
            {
              errors.Add($"[{pathParts[2]}]: {result.ErrorMessage}");
            }
          }

          t.Commit();
        })
        .ConfigureAwait(false);

      if (errors.Count > 0)
      {
        await _baseBinding.Commands.SetGlobalNotification(
          ToastNotificationType.WARNING,
          "Update Completed with Issues",
          $"Applied {successCount} updates. Encountered {errors.Count} errors: {string.Join(" | ", errors.Take(3))}",
          autoClose: false
        );
      }
      else
      {
        await _baseBinding.Commands.SetGlobalNotification(
          ToastNotificationType.SUCCESS,
          "Parameters Updated",
          $"Successfully applied {successCount} updates."
        );
      }
    }
    catch (Exception ex)
    {
      _topLevelExceptionHandler.CatchUnhandled(
        () => throw new SpeckleException("Failed to apply parameter updates", ex)
      );
    }
  }
}

public class ParameterChangesWrapper
{
  public List<ParameterChangeRequest>? Changes { get; set; }
}
