using Microsoft.Extensions.Logging;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Sdk;
using AAEC = Autodesk.Aec;
using AAECPDB = Autodesk.Aec.PropertyData.DatabaseServices;
using ADB = Autodesk.AutoCAD.DatabaseServices;

namespace Speckle.Connectors.Civil3dShared.HostApp;

/// <summary>
/// Creates new properties on Civil3D entities via a Speckle-managed PropertySetDefinition
/// stored directly in the DWG database. The property set is named "Speckle" and all
/// created properties are Text type.
/// </summary>
public class Civil3dParameterCreator
{
  private const string SPECKLE_SET_NAME = "Speckle";
  private const string PROP_SET_DEF_DICT_NAME = "AecPropertySetDefs";

  private readonly ILogger<Civil3dParameterCreator> _logger;

  public Civil3dParameterCreator(ILogger<Civil3dParameterCreator> logger)
  {
    _logger = logger;
  }

  /// <summary>
  /// Creates the named property in the "Speckle" PropertySetDefinition (creating the
  /// definition itself if it doesn't exist), attaches it to the entity, and sets its value.
  /// Must be called inside an open transaction.
  /// </summary>
  public UpdateResult CreateAndSet(
    ADB.Entity entity,
    ADB.Transaction tr,
    ADB.Database db,
    string propName,
    object? value
  )
  {
    try
    {
      var psdId = GetOrCreatePropertySetDefinition(db, tr);
      if (psdId.IsNull)
      {
        return UpdateResult.Fail("Could not get or create the Speckle PropertySetDefinition");
      }

      var defResult = EnsurePropertyDefinition(db, tr, psdId, propName);
      if (!defResult.IsSuccess)
      {
        return defResult;
      }

      EnsurePropertySetAttached(entity, psdId);

      return SetValue(entity, tr, psdId, propName, value);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogWarning(ex, "Failed to create property '{PropName}' on entity", propName);
      return UpdateResult.Fail($"Exception creating property '{propName}': {ex.Message}");
    }
  }

  /// <summary>
  /// Finds the "Speckle" PropertySetDefinition in the drawing database, or creates it
  /// if it doesn't exist yet.
  /// </summary>
  private ADB.ObjectId GetOrCreatePropertySetDefinition(ADB.Database db, ADB.Transaction tr)
  {
    // The PSD dictionary lives under the Named Objects Dictionary
    var nod = (ADB.DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, ADB.OpenMode.ForRead);

    if (nod.Contains(PROP_SET_DEF_DICT_NAME))
    {
      var psdDict = (ADB.DBDictionary)tr.GetObject(
        nod.GetAt(PROP_SET_DEF_DICT_NAME),
        ADB.OpenMode.ForRead
      );

      if (psdDict.Contains(SPECKLE_SET_NAME))
      {
        return psdDict.GetAt(SPECKLE_SET_NAME);
      }
    }

    // Create and register the PropertySetDefinition
    var psd = new AAECPDB.PropertySetDefinition();
    psd.SetToStandard(db);
    psd.SubSetDatabaseDefaults(db);
    psd.AppliesToAll = true;

    using AAECPDB.DictionaryPropertySetDefinitions propSetDefs = new(db);
    propSetDefs.AddNewRecord(SPECKLE_SET_NAME, psd);
    tr.AddNewlyCreatedDBObject(psd, true);

    return psd.ObjectId;
  }

  /// <summary>
  /// Adds a Text PropertyDefinition with the given name to the PropertySetDefinition
  /// if one doesn't already exist.
  /// </summary>
  private UpdateResult EnsurePropertyDefinition(
    ADB.Database db,
    ADB.Transaction tr,
    ADB.ObjectId psdId,
    string propName
  )
  {
    var psd = (AAECPDB.PropertySetDefinition)tr.GetObject(psdId, ADB.OpenMode.ForRead);

    foreach (AAECPDB.PropertyDefinition propDef in psd.Definitions)
    {
      if (propDef.Name == propName)
      {
        return UpdateResult.Success(); // Already defined
      }
    }

    psd.UpgradeOpen();

    var newPropDef = new AAECPDB.PropertyDefinition
    {
      DataType = AAEC.PropertyData.DataType.Text,
      Name = propName
    };
    newPropDef.SetToStandard(db);
    newPropDef.SubSetDatabaseDefaults(db);
    newPropDef.DefaultData = string.Empty;

    psd.Definitions.Add(newPropDef);

    return UpdateResult.Success();
  }

  /// <summary>
  /// Attaches the PropertySet to the entity if it isn't already attached.
  /// </summary>
  private void EnsurePropertySetAttached(ADB.Entity entity, ADB.ObjectId psdId)
  {
    try
    {
      var existingId = AAECPDB.PropertyDataServices.GetPropertySet(entity, psdId);
      if (!existingId.IsNull)
      {
        return; // Already attached
      }
    }
    catch (Autodesk.AutoCAD.Runtime.Exception)
    {
      // Not attached — fall through to attach below
    }

    if (!entity.IsWriteEnabled)
    {
      entity.UpgradeOpen();
    }

    AAECPDB.PropertyDataServices.AddPropertySet(entity, psdId);
  }

  /// <summary>
  /// Sets the named property value on the entity's Speckle PropertySet.
  /// </summary>
  private UpdateResult SetValue(
    ADB.Entity entity,
    ADB.Transaction tr,
    ADB.ObjectId psdId,
    string propName,
    object? value
  )
  {
    var propertySetId = AAECPDB.PropertyDataServices.GetPropertySet(entity, psdId);
    if (propertySetId.IsNull)
    {
      return UpdateResult.Fail("Property set could not be found on entity after attachment");
    }

    var propertySet = (AAECPDB.PropertySet)tr.GetObject(propertySetId, ADB.OpenMode.ForRead);
    var setDefinition = (AAECPDB.PropertySetDefinition)tr.GetObject(psdId, ADB.OpenMode.ForRead);

    foreach (AAECPDB.PropertyDefinition propDef in setDefinition.Definitions)
    {
      if (propDef.Name != propName)
      {
        continue;
      }

      propertySet.UpgradeOpen();
      propertySet.SetAt(propDef.Id, value?.ToString() ?? string.Empty);
      return UpdateResult.Success();
    }

    return UpdateResult.Fail($"Property '{propName}' not found on entity after creation");
  }
}
