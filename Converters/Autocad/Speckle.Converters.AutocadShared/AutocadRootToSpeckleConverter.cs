using Autodesk.AutoCAD.DatabaseServices;
using Speckle.Converters.AutocadShared.ToSpeckle;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad;

public class AutocadRootToSpeckleConverter : IRootToSpeckleConverter
{
  private readonly IConverterManager<IToSpeckleTopLevelConverter> _toSpeckle;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;
  private readonly PropertiesExtractor _propertiesExtractor;

  public AutocadRootToSpeckleConverter(
    IConverterManager<IToSpeckleTopLevelConverter> toSpeckle,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore,
    PropertiesExtractor propertiesExtractor
  )
  {
    _toSpeckle = toSpeckle;
    _settingsStore = settingsStore;
    _propertiesExtractor = propertiesExtractor;
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

    using (var l = _settingsStore.Current.Document.LockDocument())
    {
      using (var tr = _settingsStore.Current.Document.Database.TransactionManager.StartTransaction())
      {
        var objectConverter = _toSpeckle.ResolveConverter(type);

        var convertedObject = objectConverter.Convert(dbObject);

        // add properties
        Dictionary<string, object?> properties = _propertiesExtractor.GetProperties((Entity)dbObject);
        if (properties.Count > 0)
        {
          convertedObject["properties"] = properties;
        }

        tr.Commit();
        return convertedObject;
      }
    }
  }
}
