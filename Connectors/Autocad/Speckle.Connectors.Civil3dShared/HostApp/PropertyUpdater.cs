using Microsoft.Extensions.Logging;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Sdk;
using AAEC = Autodesk.Aec;
using AAECPDB = Autodesk.Aec.PropertyData.DatabaseServices;
using ADB = Autodesk.AutoCAD.DatabaseServices;

namespace Speckle.Connectors.Civil3dShared.HostApp;

/// <summary>
/// Updates Civil3D PropertySet property values. Scoped to the "Property Sets" channel only.
/// </summary>
public class PropertyUpdater
{
  private const string PROPERTY_SETS_KEY = "Property Sets";

  private readonly ILogger<PropertyUpdater> _logger;

  public PropertyUpdater(ILogger<PropertyUpdater> logger)
  {
    _logger = logger;
  }

  public UpdateResult Update(
    ADB.Entity entity,
    string[] path,
    object? newValue,
    ADB.Transaction tr,
    string? internalDefinitionName = null
  )
  {
    if (path.Length != 3 || path[0] != PROPERTY_SETS_KEY)
    {
      return UpdateResult.Fail(
        $"Unsupported path '{string.Join(".", path)}'. Only 'Property Sets.<set>.<property>' is supported."
      );
    }

    var targetSetName = path[1];
    var targetPropName = path[2];

    try
    {
      var propertySetIds = AAECPDB.PropertyDataServices.GetPropertySets(entity);
      if (propertySetIds is not null)
      {
        foreach (ADB.ObjectId psId in propertySetIds)
        {
          var propertySet = (AAECPDB.PropertySet)tr.GetObject(psId, ADB.OpenMode.ForRead);
          var setDefinition = (AAECPDB.PropertySetDefinition)
            tr.GetObject(propertySet.PropertySetDefinition, ADB.OpenMode.ForRead);

          if (setDefinition.Name != targetSetName)
          {
            continue;
          }

          var propDef = FindPropertyDefinition(propertySet, setDefinition, targetPropName, internalDefinitionName);
          if (propDef is not null)
          {
            propertySet.UpgradeOpen();
            propertySet.SetAt(propDef.Id, CoerceValue(newValue, propDef.DataType));
            return UpdateResult.Success();
          }
        }
      }

      return UpdateResult.Fail($"Property '{targetSetName}.{targetPropName}' not found on entity {entity.Handle}.");
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogWarning(
        ex,
        "Failed to update property set {Set}.{Property} on entity {Handle}",
        targetSetName,
        targetPropName,
        entity.Handle
      );
      return UpdateResult.Fail($"API rejected: {ex.Message}");
    }
  }

  private static AAECPDB.PropertyDefinition? FindPropertyDefinition(
    AAECPDB.PropertySet propertySet,
    AAECPDB.PropertySetDefinition setDefinition,
    string targetPropName,
    string? internalDefinitionName
  )
  {
    if (internalDefinitionName is { Length: > 0 } internalName)
    {
      var internalMatch = FindPropertyDefinitionByInternalName(propertySet, setDefinition, internalName);
      if (internalMatch is not null)
      {
        return internalMatch;
      }
    }

    foreach (AAECPDB.PropertyDefinition propDef in setDefinition.Definitions)
    {
      if (propDef.Name == targetPropName)
      {
        return propDef;
      }
    }

    return null;
  }

  private static AAECPDB.PropertyDefinition? FindPropertyDefinitionByInternalName(
    AAECPDB.PropertySet propertySet,
    AAECPDB.PropertySetDefinition setDefinition,
    string internalDefinitionName
  )
  {
    foreach (AAECPDB.PropertySetData data in propertySet.PropertySetData)
    {
      if (data.FieldBucketId == internalDefinitionName || data.Id.ToString() == internalDefinitionName)
      {
        return FindPropertyDefinitionById(setDefinition, data.Id);
      }
    }

    return null;
  }

  private static AAECPDB.PropertyDefinition? FindPropertyDefinitionById(
    AAECPDB.PropertySetDefinition setDefinition,
    int propertyDefinitionId
  )
  {
    foreach (AAECPDB.PropertyDefinition propDef in setDefinition.Definitions)
    {
      if (propDef.Id == propertyDefinitionId)
      {
        return propDef;
      }
    }

    return null;
  }

  private static object? CoerceValue(object? value, AAEC.PropertyData.DataType dataType)
  {
    if (value is null)
    {
      return null;
    }

    return dataType switch
    {
      AAEC.PropertyData.DataType.Integer => Convert.ToInt32(value),
      AAEC.PropertyData.DataType.AutoIncrement => Convert.ToInt32(value),
      AAEC.PropertyData.DataType.Real => Convert.ToDouble(value),
      AAEC.PropertyData.DataType.TrueFalse => Convert.ToBoolean(value),
      _ => value.ToString(),
    };
  }
}
