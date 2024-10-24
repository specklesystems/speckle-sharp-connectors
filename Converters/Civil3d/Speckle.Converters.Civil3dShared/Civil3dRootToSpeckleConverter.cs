using System.ComponentModel.DataAnnotations;
using Autodesk.AutoCAD.DatabaseServices;
using Speckle.Converters.Civil3dShared.ToSpeckle;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Sdk;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Civil3dShared;

public class Civil3dRootToSpeckleConverter : IRootToSpeckleConverter
{
  private readonly IConverterManager<IToSpeckleTopLevelConverter> _toSpeckle;
  private readonly IConverterSettingsStore<Civil3dConversionSettings> _settingsStore;
  private readonly PartDataExtractor _partDataExtractor;
  private readonly PropertySetExtractor _propertySetExtractor;
  private readonly GeneralPropertiesExtractor _generalPropertiesExtractor;
  private readonly ExtensionDictionaryExtractor _extensionDictionaryExtractor;

  public Civil3dRootToSpeckleConverter(
    IConverterManager<IToSpeckleTopLevelConverter> toSpeckle,
    IConverterSettingsStore<Civil3dConversionSettings> settingsStore,
    PartDataExtractor partDataExtractor,
    PropertySetExtractor propertySetExtractor,
    GeneralPropertiesExtractor generalPropertiesExtractor,
    ExtensionDictionaryExtractor extensionDictionaryExtractor
  )
  {
    _toSpeckle = toSpeckle;
    _settingsStore = settingsStore;
    _partDataExtractor = partDataExtractor;
    _propertySetExtractor = propertySetExtractor;
    _generalPropertiesExtractor = generalPropertiesExtractor;
    _extensionDictionaryExtractor = extensionDictionaryExtractor;
  }

  public Base Convert(object target)
  {
    if (target is not DBObject dbObject)
    {
      throw new ValidationException(
        $"Conversion of {target.GetType().Name} to Speckle is not supported. Only objects that inherit from DBObject are."
      );
    }

    Type type = dbObject.GetType();
    object objectToConvert = dbObject;
    Dictionary<string, object?> properties = new();

    // check first for civil type objects
    if (target is CDB.Entity civilEntity)
    {
      type = civilEntity.GetType();
      objectToConvert = civilEntity;

      // get properties like partdata, property sets, general properties
      properties = GetCivilEntityProperties(civilEntity);
    }

    var objectConverter = _toSpeckle.ResolveConverter(type, true);

    try
    {
      using (var l = _settingsStore.Current.Document.LockDocument())
      {
        using (var tr = _settingsStore.Current.Document.Database.TransactionManager.StartTransaction())
        {
          var result = objectConverter.Convert(objectToConvert);

          if (properties.Count > 0)
          {
            result["properties"] = properties;
          }

          tr.Commit();
          return result;
        }
      }
    }
    catch (SpeckleException e)
    {
      Console.WriteLine(e);
      throw; // Just rethrowing for now, Logs may be needed here.
    }
  }

  private Dictionary<string, object?> GetCivilEntityProperties(CDB.Entity entity)
  {
    Dictionary<string, object?> properties = new();

    AddDictionaryToPropertyDictionary(
      _generalPropertiesExtractor.GetGeneralProperties(entity),
      "General Properties",
      properties
    );
    AddDictionaryToPropertyDictionary(_partDataExtractor.GetPartData(entity), "Part Data", properties);
    AddDictionaryToPropertyDictionary(_propertySetExtractor.GetPropertySets(entity), "Property Sets", properties);
    AddDictionaryToPropertyDictionary(
      _extensionDictionaryExtractor.GetExtensionDictionary(entity),
      "Extension Dictionary",
      properties
    );

    return properties;
  }

  private void AddDictionaryToPropertyDictionary(
    Dictionary<string, object?>? entryDictionary,
    string entryName,
    Dictionary<string, object?> propertyDictionary
  )
  {
    if (entryDictionary is not null && entryDictionary.Count > 0)
    {
      propertyDictionary.Add(entryName, entryDictionary);
    }
  }
}
