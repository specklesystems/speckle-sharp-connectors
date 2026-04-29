using Microsoft.Extensions.Logging;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Services;
using Speckle.Sdk;
using DB = Autodesk.Revit.DB;

namespace Speckle.Connectors.Revit.HostApp;

/// <summary>
/// Updates parameter values on Revit elements. Mirrors the structure from ParameterExtractor.
/// Path format: ["Instance Parameters" | "Type Parameters" | "System Type Parameters", "GroupName", "ParameterName"]
/// </summary>
public class ParameterUpdater
{
  private readonly RevitContext _revitContext;
  private readonly ScalingServiceToHost _scalingServiceToHost;
  private readonly ILogger<ParameterUpdater> _logger;

  public ParameterUpdater(
    RevitContext revitContext,
    ScalingServiceToHost scalingServiceToHost,
    ILogger<ParameterUpdater> logger
  )
  {
    _revitContext = revitContext;
    _scalingServiceToHost = scalingServiceToHost;
    _logger = logger;
  }

  public UpdateResult Update(DB.Element element, string[] path, object? newValue, string? internalDefinitionName = null)
  {
    // path = ["Instance Parameters", "Identity Data", "Mark"]
    if (path.Length != 3)
    {
      return UpdateResult.Fail(
        $"Path must have exactly 3 segments: [scope, group, parameter]. Got: {string.Join(" → ", path)}"
      );
    }

    var parameterScope = path[0]; // "Instance Parameters" | "Type Parameters" | "System Type Parameters"
    var groupName = path[1]; // "Identity Data", "Dimensions", etc.
    var parameterKey = path[2]; // human-readable name (or internalDefinitionName if collision)

    // get target element based on scope
    var targetElement = GetTargetElement(element, parameterScope);
    if (targetElement == null)
    {
      return UpdateResult.Fail($"Could not resolve target for scope: {parameterScope}");
    }

    // find the parameter (now using the robust lookup)
    var parameter = FindParameter(targetElement, groupName, parameterKey, internalDefinitionName);
    if (parameter == null)
    {
      return UpdateResult.Fail($"Parameter not found: {parameterKey} in group {groupName}");
    }

    if (parameter.IsReadOnly)
    {
      return UpdateResult.Fail($"Parameter '{parameterKey}' is readonly in Revit");
    }

    return SetParameterValue(parameter, newValue);
  }

  private DB.Element? GetTargetElement(DB.Element element, string scope) =>
    scope switch
    {
      "Instance Parameters" => element,
      "Type Parameters" => GetTypeElement(element),
      "System Type Parameters" => GetSystemTypeElement(element),
      _ => null,
    };

  private DB.Element? GetTypeElement(DB.Element element)
  {
    var typeId = element.GetTypeId();
    if (typeId == DB.ElementId.InvalidElementId)
    {
      return null;
    }
    return _revitContext.UIApplication?.ActiveUIDocument.Document.GetElement(typeId);
  }

  private DB.Element? GetSystemTypeElement(DB.Element element)
  {
    var system = GetMEPSystem(element);
    if (system == null)
    {
      return null;
    }

    return _revitContext.UIApplication?.ActiveUIDocument.Document.GetElement(system.GetTypeId());
  }

  private DB.MEPSystem? GetMEPSystem(DB.Element element)
  {
    if (element is DB.MEPCurve curve)
    {
      return curve.MEPSystem;
    }

    if (element is DB.FamilyInstance fi)
    {
      var cm = fi.MEPModel?.ConnectorManager;
      if (cm != null)
      {
        foreach (DB.Connector conn in cm.Connectors)
        {
          if (conn.ConnectorType == DB.ConnectorType.Physical && conn.IsConnected && conn.MEPSystem != null)
          {
            return conn.MEPSystem;
          }
        }
      }
    }

    return null;
  }

  private DB.Parameter? FindParameter(
    DB.Element element,
    string groupName,
    string parameterKey,
    string? internalDefinitionName
  )
  {
    // fast path: direct lookup using the internal definition name
    if (!string.IsNullOrEmpty(internalDefinitionName))
    {
      // try as BuiltInParameter enum
      if (Enum.TryParse(internalDefinitionName, out DB.BuiltInParameter bip) && bip != DB.BuiltInParameter.INVALID)
      {
        var param = element.get_Parameter(bip);
        if (param != null)
        {
          return param;
        }
      }

      // try as shared parameter Guid
      if (Guid.TryParse(internalDefinitionName, out Guid guid))
      {
        var param = element.get_Parameter(guid);
        if (param != null)
        {
          return param;
        }
      }
    }

    // fallback: iteration for project parameters or missing internal names
    DB.Parameter? fallbackParameter = null;

    foreach (DB.Parameter parameter in element.Parameters)
    {
      var definition = parameter.Definition;
      if (definition == null)
      {
        continue;
      }

      var currentInternalName = GetInternalDefinitionName(parameter);
      var humanName = definition.Name;

      // exact internal name match (covers project params that aren't BuiltIn/Shared)
      if (!string.IsNullOrEmpty(internalDefinitionName) && currentInternalName == internalDefinitionName)
      {
        return parameter;
      }

      // fallback human-readable name matching
      if (humanName == parameterKey || currentInternalName == parameterKey)
      {
        var paramGroup = definition.GetGroupTypeId();
        var groupLabel = DB.LabelUtils.GetLabelForGroup(paramGroup);

        if (groupLabel == groupName)
        {
          return parameter;
        }
        fallbackParameter ??= parameter;
      }
    }

    return fallbackParameter;
  }

  private string GetInternalDefinitionName(DB.Parameter parameter)
  {
    if (parameter.Definition is DB.InternalDefinition internalDef)
    {
      var bip = internalDef.BuiltInParameter;
      if (bip != DB.BuiltInParameter.INVALID)
      {
        return bip.ToString();
      }
    }

    return parameter.Definition.Name;
  }

  private UpdateResult SetParameterValue(DB.Parameter parameter, object? newValue)
  {
    var paramName = parameter.Definition.Name;
    if (newValue == null)
    {
      if (parameter.StorageType == DB.StorageType.String)
      {
        return parameter.Set(string.Empty)
          ? UpdateResult.Success()
          : UpdateResult.Fail("Failed to clear string parameter");
      }
      return UpdateResult.Fail("Cannot set non-string parameter to null");
    }

    try
    {
      var success = parameter.StorageType switch
      {
        DB.StorageType.String => parameter.Set(newValue.ToString()),
        DB.StorageType.Integer => SetIntegerValue(parameter, newValue),
        DB.StorageType.Double => SetDoubleValue(parameter, newValue),
        DB.StorageType.ElementId => SetElementIdValue(parameter, newValue),
        _ => false,
      };

      return success ? UpdateResult.Success() : UpdateResult.Fail($"Failed to set parameter value to: {newValue}");
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogWarning(ex, "Failed to set parameter value");
      return UpdateResult.Fail($"Exception for '{paramName}': {ex.Message}");
    }
  }

  private bool SetIntegerValue(DB.Parameter parameter, object newValue)
  {
    if (newValue is int i)
    {
      return parameter.Set(i);
    }

    if (newValue is bool b)
    {
      return parameter.Set(b ? 1 : 0);
    }

    if (int.TryParse(newValue.ToString(), out var parsed))
    {
      return parameter.Set(parsed);
    }

    var strValue = newValue.ToString();
    if (strValue == "Yes")
    {
      return parameter.Set(1);
    }
    if (strValue == "No")
    {
      return parameter.Set(0);
    }

    return parameter.SetValueString(strValue);
  }

  private bool SetDoubleValue(DB.Parameter parameter, object newValue)
  {
    double doubleValue;

    if (newValue is double d)
    {
      doubleValue = d;
    }
    else if (newValue is int intVal)
    {
      doubleValue = intVal;
    }
    else if (double.TryParse(newValue.ToString(), out var parsed))
    {
      doubleValue = parsed;
    }
    else
    {
      return false;
    }

    var internalValue = _scalingServiceToHost.ScaleToNative(doubleValue, parameter.GetUnitTypeId());
    return parameter.Set(internalValue);
  }

  private bool SetElementIdValue(DB.Parameter parameter, object newValue)
  {
    if (newValue is DB.ElementId eid)
    {
      return parameter.Set(eid);
    }

    // TODO: check this fckr later

    //     if (newValue is long idInt)
    //     {
    // #if REVIT2024_OR_GREATER
    //       return parameter.Set(new DB.ElementId(idInt));
    // #else
    //       return parameter.Set(new DB.ElementId((long)idInt));
    // #endif
    //     }
    //
    //     if (long.TryParse(newValue.ToString(), out var parsedId))
    //     {
    // #if REVIT2024_OR_GREATER
    //       return parameter.Set(new DB.ElementId(parsedId));
    // #else
    //       return parameter.Set(new DB.ElementId((long)parsedId));
    // #endif
    //     }

    var elementName = newValue.ToString();
    if (elementName != null)
    {
      var foundElement = FindElementByName(elementName);
      if (foundElement != null)
      {
        return parameter.Set(foundElement.Id);
      }
    }

    return false;
  }

  private DB.Element? FindElementByName(string name)
  {
    var doc = _revitContext.UIApplication?.ActiveUIDocument.Document;

    using var materialCollector = new DB.FilteredElementCollector(doc);
    var material = materialCollector.OfClass(typeof(DB.Material)).FirstOrDefault(e => e.Name == name);
    if (material != null)
    {
      return material;
    }

    using var levelCollector = new DB.FilteredElementCollector(doc);
    var level = levelCollector.OfClass(typeof(DB.Level)).FirstOrDefault(e => e.Name == name);
    if (level != null)
    {
      return level;
    }
    using var phaseCollector = new DB.FilteredElementCollector(doc);
    var phase = phaseCollector.OfClass(typeof(DB.Phase)).FirstOrDefault(e => e.Name == name);
    if (phase != null)
    {
      return phase;
    }

    return null;
  }
}

// TODO: we will see, extract this guy out
public readonly struct UpdateResult
{
  public bool IsSuccess { get; }
  public string? ErrorMessage { get; }

  private UpdateResult(bool success, string? error)
  {
    IsSuccess = success;
    ErrorMessage = error;
  }

  public static UpdateResult Success() => new(true, null);

  public static UpdateResult Fail(string message) => new(false, message);
}
