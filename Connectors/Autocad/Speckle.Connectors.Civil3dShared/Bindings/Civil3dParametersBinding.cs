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
  private readonly Civil3dParameterCreator _parameterCreator;

  public Civil3dParametersBinding(
    IBrowserBridge parent,
    IThreadContext threadContext,
    ITopLevelExceptionHandler topLevelExceptionHandler,
    IJsonSerializer jsonSerializer,
    IBasicConnectorBinding baseBinding,
    PropertyUpdater propertyUpdater,
    Civil3dParameterCreator parameterCreator
  )
  {
    Parent = parent;
    _threadContext = threadContext;
    _topLevelExceptionHandler = topLevelExceptionHandler;
    _jsonSerializer = jsonSerializer;
    _baseBinding = baseBinding;
    _propertyUpdater = propertyUpdater;
    _parameterCreator = parameterCreator;
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
            if (!TryResolveEntity(doc, tr, request, out var entity, out var entityError))
            {
              errors.Add(entityError!);
              continue;
            }

            object? rawValue = request.To is Newtonsoft.Json.Linq.JValue jValue ? jValue.Value : request.To;

            // a creation flag only says the widget saw no value for this object; when the path resolves to a
            // property the entity does have it, so it is updated rather than shadowed by a new one
            var hasPropertyPath = TryParsePath(request.Path, out var parsedPath, out var pathError);
            if (
              hasPropertyPath
              && (
                !request.IsCreation
                || _propertyUpdater.Exists(entity!, parsedPath!.ToArray(), tr, request.InternalDefinitionName)
              )
            )
            {
              Record(
                _propertyUpdater.Update(entity!, parsedPath!.ToArray(), rawValue, tr, request.InternalDefinitionName)
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
              errors.Add($"Invalid path for new property: '{request.Path}'");
              continue;
            }

            Record(_parameterCreator.CreateAndSet(entity!, tr, doc.Database, paramName, rawValue));
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

  private static bool TryResolveEntity(
    Document doc,
    ADB.Transaction tr,
    ParameterChangeRequest request,
    out ADB.Entity? entity,
    out string? errorMessage
  )
  {
    entity = null;
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
      return true;
    }
    catch (Autodesk.AutoCAD.Runtime.Exception e) when (e.ErrorStatus == Autodesk.AutoCAD.Runtime.ErrorStatus.WasErased)
    {
      errorMessage = $"Object was erased: {request.ApplicationId}";
      return false;
    }
  }

  private static bool TryParsePath(string? path, out ParsedPropertyPath? parsedPath, out string? errorMessage)
  {
    parsedPath = null;
    errorMessage = null;

    if (string.IsNullOrEmpty(path))
    {
      errorMessage = "Parameter path is missing";
      return false;
    }

    var pathParts = StripPrefix(path!).Split(['.'], 3);
    if (pathParts.Length != 3)
    {
      errorMessage = "Parameter path is incorrectly formatted";
      return false;
    }

    parsedPath = new ParsedPropertyPath(pathParts[0], pathParts[1], pathParts[2]);
    return true;
  }

  private static string StripPrefix(string path)
  {
    if (path.StartsWith("properties.", StringComparison.OrdinalIgnoreCase))
    {
      return path[11..];
    }

    if (path.StartsWith("parameters.", StringComparison.OrdinalIgnoreCase))
    {
      return path[11..];
    }

    return path;
  }

  /// <summary>
  /// Strips the "properties." or "parameters." prefix added by the widget and returns
  /// the bare property name (e.g. "properties.SpeckleTag" → "SpeckleTag").
  /// </summary>
  private static string ExtractCreationParamName(string path)
  {
    var name = StripPrefix(path).Trim();
    var parts = name.Split(['.'], 3);
    if (parts.Length == 3 && parts[0] == PropertyUpdater.PROPERTY_SETS_KEY)
    {
      // a creation on an existing property-set path that did not resolve is named after the property, not the path
      return parts[2].Trim();
    }

    return name;
  }
}
