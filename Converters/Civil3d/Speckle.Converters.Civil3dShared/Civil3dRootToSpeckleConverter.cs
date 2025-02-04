using System.ComponentModel.DataAnnotations;
using Speckle.Converters.AutocadShared.ToSpeckle;
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
  private readonly PropertiesExtractor _propertiesExtractor;

  public Civil3dRootToSpeckleConverter(
    IConverterManager<IToSpeckleTopLevelConverter> toSpeckle,
    IConverterSettingsStore<Civil3dConversionSettings> settingsStore,
    PropertiesExtractor propertiesExtractor
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

    // check first for civil type objects
    // POC: some classes (eg Civil.DatabaseServices.CogoPoint) actually inherit from Autocad.DatabaseServices.Entity instead of Civil!!
    // These need top level converters in Civil for now, but in the future we should implement a EntityToSpeckleTopLevelConverter for Autocad as well.
    if (target is CDB.Entity civilEntity)
    {
      type = civilEntity.GetType();
    }

    var objectConverter = _toSpeckle.ResolveConverter(type);

    try
    {
      using (var l = _settingsStore.Current.Document.LockDocument())
      {
        using (var tr = _settingsStore.Current.Document.Database.TransactionManager.StartTransaction())
        {
          var result = objectConverter.Convert(target);

          // we need to capture properties on autocad entities
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
