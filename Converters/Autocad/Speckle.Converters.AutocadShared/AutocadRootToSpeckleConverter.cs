using Autodesk.AutoCAD.DatabaseServices;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.Autocad;

public class AutocadRootToSpeckleConverter : IRootToSpeckleConverter
{
  private readonly IConverterManager<IToSpeckleTopLevelConverter> _toSpeckle;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public AutocadRootToSpeckleConverter(
    IConverterManager<IToSpeckleTopLevelConverter> toSpeckle,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  )
  {
    _toSpeckle = toSpeckle;
    _settingsStore = settingsStore;
  }

  public BaseResult Convert(object target)
  {
    if (target is not DBObject dbObject)
    {
      throw new ValidationException(
        $"Conversion of {target.GetType().Name} to Speckle is not supported. Only objects that inherit from DBObject are."
      );
    }

    Type type = dbObject.GetType();

    using (var l = _settingsStore.Current.Document.LockDocument())
    {
      using (var tr = _settingsStore.Current.Document.Database.TransactionManager.StartTransaction())
      {
        var result = _toSpeckle.ResolveConverter(type);
        if (result.IsFailure)
        {
          return BaseResult.Failure(result);
        }
        var convertedObject = result.Converter.Convert(dbObject);
        tr.Commit();
        return convertedObject;
      }
    }
  }
}
