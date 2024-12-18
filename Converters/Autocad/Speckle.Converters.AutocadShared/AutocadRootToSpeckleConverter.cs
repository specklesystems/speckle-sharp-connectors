using Autodesk.AutoCAD.DatabaseServices;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Registration;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad;

public class AutocadRootToSpeckleConverter : SpeckleConverter
{
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public AutocadRootToSpeckleConverter(
    IConverterManager toSpeckle,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  ) : base(toSpeckle)
  {
    _settingsStore = settingsStore;
  }

  public override Base Convert(object target)
  {

    if (target is not DBObject)
    {
      Type type = target.GetType();
      throw new ValidationException(
        $"Conversion of {type.Name} to Speckle is not supported. Only objects that inherit from DBObject are."
      );
    }

    using (var l = _settingsStore.Current.Document.LockDocument())
    {
      using (var tr = _settingsStore.Current.Document.Database.TransactionManager.StartTransaction())
      {
        var convertedObject=  base.Convert(target);
        tr.Commit();
        return convertedObject;
      }
    }
  }
}
