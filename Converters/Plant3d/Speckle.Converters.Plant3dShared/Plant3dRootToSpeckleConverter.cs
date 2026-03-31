using System.ComponentModel.DataAnnotations;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Objects.Data;
using Speckle.Sdk;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Plant3dShared;

public class Plant3dRootToSpeckleConverter : IRootToSpeckleConverter
{
  private readonly IConverterManager<IToSpeckleTopLevelConverter> _toSpeckle;
  private readonly IConverterSettingsStore<Plant3dConversionSettings> _settingsStore;
  private readonly ToSpeckle.PropertiesExtractor _propertiesExtractor;
  private readonly ToSpeckle.Plant3dDataExtractor _dataExtractor;

  public Plant3dRootToSpeckleConverter(
    IConverterManager<IToSpeckleTopLevelConverter> toSpeckle,
    IConverterSettingsStore<Plant3dConversionSettings> settingsStore,
    ToSpeckle.PropertiesExtractor propertiesExtractor,
    ToSpeckle.Plant3dDataExtractor dataExtractor
  )
  {
    _toSpeckle = toSpeckle;
    _settingsStore = settingsStore;
    _propertiesExtractor = propertiesExtractor;
    _dataExtractor = dataExtractor;
  }

  public Base Convert(object target)
  {
    if (target is not ADB.DBObject dbObject)
    {
      throw new ValidationException(
        $"Conversion of {target.GetType().Name} to Speckle is not supported. Only objects that inherit from DBObject are."
      );
    }

    Type type = dbObject.GetType();

    var objectConverter = _toSpeckle.ResolveConverter(type);

    try
    {
      using (var l = _settingsStore.Current.Document.LockDocument())
      {
        using (var tr = _settingsStore.Current.Document.Database.TransactionManager.StartTransaction())
        {
          var result = objectConverter.Convert(target);

          if (target is ADB.Entity autocadEntity)
          {
            // Extract AEC property sets and extension dictionaries
            var properties = _propertiesExtractor.GetProperties(autocadEntity);

            // Extract Plant3D project database properties (Tag, NominalDiameter, etc.)
            var dataProperties = _dataExtractor.GetDataProperties(autocadEntity);

            if (result is DataObject dataObject)
            {
              // Merge AEC properties
              foreach (var kvp in properties)
              {
                dataObject.properties[kvp.Key] = kvp.Value;
              }

              // Merge Plant3D data under "P&ID" key
              if (dataProperties.Count > 0)
              {
                dataObject.properties["P&ID"] = dataProperties;
              }
            }
            else
            {
              if (properties.Count > 0)
              {
                result["properties"] = properties;
              }
              if (dataProperties.Count > 0)
              {
                result["Plant3D Data"] = dataProperties;
              }
            }
          }

          tr.Commit();
          return result;
        }
      }
    }
    catch (SpeckleException e)
    {
      Console.WriteLine(e);
      throw;
    }
  }
}
