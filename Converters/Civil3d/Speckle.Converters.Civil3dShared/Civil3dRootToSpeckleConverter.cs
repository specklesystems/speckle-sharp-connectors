using System.ComponentModel.DataAnnotations;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;

namespace Speckle.Converters.Civil3dShared;

public class Civil3dRootToSpeckleConverter : IRootToSpeckleConverter
{
  private readonly IConverterManager<IToSpeckleTopLevelConverter> _toSpeckle;
  private readonly IConverterSettingsStore<Civil3dConversionSettings> _settingsStore;

  public Civil3dRootToSpeckleConverter(
    IConverterManager<IToSpeckleTopLevelConverter> toSpeckle,
    IConverterSettingsStore<Civil3dConversionSettings> settingsStore
  )
  {
    _toSpeckle = toSpeckle;
    _settingsStore = settingsStore;
  }

  public BaseResult Convert(object target)
  {
    if (target is not ADB.DBObject dbObject)
    {
      throw new ValidationException(
        $"Conversion of {target.GetType().Name} to Speckle is not supported. Only objects that inherit from DBObject are."
      );
    }

    Type type = dbObject.GetType();
    object objectToConvert = dbObject;

    // check first for civil type objects
    if (target is CDB.Entity civilEntity)
    {
      type = civilEntity.GetType();
      objectToConvert = civilEntity;
    }

    var converterResult = _toSpeckle.ResolveConverter(type);
    if (converterResult.IsFailure)
    {
      return BaseResult.Failure(converterResult);
    }

      using (var l = _settingsStore.Current.Document.LockDocument())
      {
        using (var tr = _settingsStore.Current.Document.Database.TransactionManager.StartTransaction())
        {
          var result = converterResult.Converter.Convert(objectToConvert);

          tr.Commit();
          return result;
        }
      }
  }
}
