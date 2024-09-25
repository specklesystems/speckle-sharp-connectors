using Autodesk.AutoCAD.DatabaseServices;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad;

public class AutocadRootToHostConverter : IRootToSpeckleConverter
{
  private readonly IConverterManager<IToSpeckleTopLevelConverter> _toSpeckle;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public AutocadRootToHostConverter(
    IConverterManager<IToSpeckleTopLevelConverter> toSpeckle,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  )
  {
    _toSpeckle = toSpeckle;
    _settingsStore = settingsStore;
  }

  public Base Convert(object target)
  {
    if (target is not DBObject dbObject)
    {
      throw new ConversionNotSupportedException(
        $"Conversion of {target.GetType().Name} to Speckle is not supported. Only objects that inherit from DBObject are."
      );
    }

    Type type = dbObject.GetType();

    try
    {
      using (var l = _settingsStore.Current.Document.LockDocument())
      {
        using (var tr = _settingsStore.Current.Document.Database.TransactionManager.StartTransaction())
        {
          var objectConverter = _toSpeckle.ResolveConverter(type);

          if (objectConverter == null)
          {
            throw new ConversionNotSupportedException($"No conversion found for {target.GetType().Name}");
          }

          var convertedObject = objectConverter.Convert(dbObject);
          tr.Commit();
          return convertedObject;
        }
      }
    }
    catch (SpeckleConversionException e)
    {
      Console.WriteLine(e);
      throw; // Just rethrowing for now, Logs may be needed here.
    }
  }
}
