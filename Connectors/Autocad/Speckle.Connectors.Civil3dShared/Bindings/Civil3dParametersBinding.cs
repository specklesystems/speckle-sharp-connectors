using Speckle.Connectors.Civil3dShared.HostApp;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Utils;
using Speckle.Sdk;
using ADB = Autodesk.AutoCAD.DatabaseServices;

namespace Speckle.Connectors.Civil3dShared.Bindings;

public record ParsedPropertyPath(string Scope, string SetName, string PropertyName)
{
  public string[] ToArray() => [Scope, SetName, PropertyName];
}

internal sealed class Civil3dParametersBinding : IParametersBinding
{
  public string Name => "parametersBinding";
  public IBrowserBridge Parent { get; }

  private readonly IThreadContext _threadContext;
  private readonly ITopLevelExceptionHandler _topLevelExceptionHandler;
  private readonly IJsonSerializer _jsonSerializer;
  private readonly IBasicConnectorBinding _baseBinding;
  private readonly PropertyUpdater _propertyUpdater;

  public Civil3dParametersBinding(
    IBrowserBridge parent,
    IThreadContext threadContext,
    ITopLevelExceptionHandler topLevelExceptionHandler,
    IJsonSerializer jsonSerializer,
    IBasicConnectorBinding baseBinding,
    PropertyUpdater propertyUpdater
  )
  {
    Parent = parent;
    _threadContext = threadContext;
    _topLevelExceptionHandler = topLevelExceptionHandler;
    _jsonSerializer = jsonSerializer;
    _baseBinding = baseBinding;
    _propertyUpdater = propertyUpdater;
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

      var doc =
        Application.DocumentManager.MdiActiveDocument
        ?? throw new SpeckleException("Unable to retrieve active document.");

      int successCount = 0;
      List<string> errors = new();

      await _threadContext
        .RunOnMainAsync(() =>
        {
          using var docLock = doc.LockDocument();
          using var tr = doc.Database.TransactionManager.StartTransaction();

          foreach (var request in requests)
          {
            if (!TryValidateAndParseRequest(doc, tr, request, out var entity, out var parsedPath, out var error))
            {
              errors.Add(error!);
              continue;
            }

            object? rawValue = request.To is Newtonsoft.Json.Linq.JValue jValue ? jValue.Value : request.To;

            var result = _propertyUpdater.Update(
              entity!,
              parsedPath!.ToArray(),
              rawValue,
              tr,
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

          tr.Commit();
          return Task.CompletedTask;
        })
        .ConfigureAwait(false);

      if (errors.Count > 0)
      {
        var groupedErrors = errors.GroupBy(e => e).Select(g => $"{g.Count()} x {g.Key}");
        var errorString = string.Join(", ", groupedErrors);

        if (successCount > 0)
        {
          await _baseBinding
            .Commands.SetGlobalNotification(
              ToastNotificationType.WARNING,
              "Parameters updated with errors",
              $"Applied {successCount} updates. Encountered {errors.Count} errors: {errorString}",
              autoClose: false
            )
            .ConfigureAwait(false);
        }
        else
        {
          await _baseBinding
            .Commands.SetGlobalNotification(
              ToastNotificationType.DANGER,
              "No parameters updated",
              $"All {errors.Count} updates failed: {errorString}",
              autoClose: false
            )
            .ConfigureAwait(false);
        }
      }
      else if (successCount > 0)
      {
        await _baseBinding
          .Commands.SetGlobalNotification(
            ToastNotificationType.SUCCESS,
            "All parameters updated",
            $"Successfully applied {successCount} updates."
          )
          .ConfigureAwait(false);
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _topLevelExceptionHandler.CatchUnhandled(() =>
        throw new SpeckleException("Failed to apply parameter updates", ex)
      );
    }
  }

  private static bool TryValidateAndParseRequest(
    Document doc,
    ADB.Transaction tr,
    ParameterChangeRequest request,
    out ADB.Entity? entity,
    out ParsedPropertyPath? parsedPath,
    out string? errorMessage
  )
  {
    entity = null;
    parsedPath = null;
    errorMessage = null;

    if (string.IsNullOrEmpty(request.ApplicationId))
    {
      errorMessage = "Missing ApplicationId.";
      return false;
    }

    if (!long.TryParse(request.ApplicationId, out long handleValue))
    {
      errorMessage = $"ApplicationId is not a valid handle: {request.ApplicationId}";
      return false;
    }

    var handle = new ADB.Handle(handleValue);
    try
    {
      if (!doc.Database.TryGetObjectId(handle, out ADB.ObjectId objectId))
      {
        errorMessage = $"ObjectId not found for handle: {request.ApplicationId}";
        return false;
      }

      if (tr.GetObject(objectId, ADB.OpenMode.ForRead) is not ADB.Entity resolved)
      {
        errorMessage = $"Object is not an Entity: {request.ApplicationId}";
        return false;
      }

      entity = resolved;
    }
    catch (Autodesk.AutoCAD.Runtime.Exception e) when (e.ErrorStatus == Autodesk.AutoCAD.Runtime.ErrorStatus.WasErased)
    {
      errorMessage = $"Object was erased: {request.ApplicationId}";
      return false;
    }

    var rawPath = request.Path;
    if (string.IsNullOrEmpty(rawPath))
    {
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

    var pathParts = rawPath.Split(['.'], 3);
    if (pathParts.Length != 3)
    {
      errorMessage = "Parameter path is incorrectly formatted";
      return false;
    }

    parsedPath = new ParsedPropertyPath(pathParts[0], pathParts[1], pathParts[2]);
    return true;
  }
}
