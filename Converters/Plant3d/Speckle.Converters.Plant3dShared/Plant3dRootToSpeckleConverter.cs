using System.ComponentModel.DataAnnotations;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Sdk;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Plant3dShared;

public class Plant3dRootToSpeckleConverter : IRootToSpeckleConverter
{
  private readonly IConverterManager<IToSpeckleTopLevelConverter> _toSpeckle;
  private readonly IConverterSettingsStore<Plant3dConversionSettings> _settingsStore;
  private readonly ToSpeckle.PropertiesExtractor _propertiesExtractor;

  public Plant3dRootToSpeckleConverter(
    IConverterManager<IToSpeckleTopLevelConverter> toSpeckle,
    IConverterSettingsStore<Plant3dConversionSettings> settingsStore,
    ToSpeckle.PropertiesExtractor propertiesExtractor
  )
  {
    _toSpeckle = toSpeckle;
    _settingsStore = settingsStore;
    _propertiesExtractor = propertiesExtractor;
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

    // TODO: Add Plant3D-specific type resolution here
    // For example, check for Plant3D entity types from Autodesk.ProcessPower namespace

    var objectConverter = _toSpeckle.ResolveConverter(type);

    try
    {
      using (var l = _settingsStore.Current.Document.LockDocument())
      {
        using (var tr = _settingsStore.Current.Document.Database.TransactionManager.StartTransaction())
        {
          var result = objectConverter.Convert(target);

          // Extract properties (property sets, extension dictionaries) for AutoCAD entities
          if (target is ADB.Entity autocadEntity)
          {
            var properties = _propertiesExtractor.GetProperties(autocadEntity);
            if (properties.Count > 0)
            {
              result["properties"] = properties;
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
      throw; // Just rethrowing for now, Logs may be needed here.
    }
  }
}
