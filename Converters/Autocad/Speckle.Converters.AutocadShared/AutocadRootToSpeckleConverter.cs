using Autodesk.AutoCAD.DatabaseServices;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad;

public class AutocadRootToSpeckleConverter : IRootToSpeckleConverter
{
  private readonly IConverterManager _toSpeckle;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public AutocadRootToSpeckleConverter(
    IConverterManager toSpeckle,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  )
  {
    _toSpeckle = toSpeckle;
    _settingsStore = settingsStore;
  }

  public Base Convert(object target)
  {

    Type type = target.GetType();
    if (target is not DBObject dbObject)
    {
      throw new ValidationException(
        $"Conversion of {type.Name} to Speckle is not supported. Only objects that inherit from DBObject are."
      );
    }

    using (var l = _settingsStore.Current.Document.LockDocument())
    {
      using (var tr = _settingsStore.Current.Document.Database.TransactionManager.StartTransaction())
      {
        var objectConverter = _toSpeckle.GetHostConverter(type);
        var interfaceType = typeof(ITypedConverter<,>).MakeGenericType(type, typeof(Base));
       var convertedObject = interfaceType.GetMethod("Convert")!.Invoke(objectConverter, new object[] { dbObject })!;

        tr.Commit();
        return (Base)convertedObject!;
      }
    }
  }
}
