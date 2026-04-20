using Autodesk.AutoCAD.DatabaseServices;
using Speckle.Converters.AutocadShared.ToSpeckle;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Objects.Data;
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

    using var l = _settingsStore.Current.Document.LockDocument();
    using var tr = _settingsStore.Current.Document.Database.TransactionManager.StartTransaction();
    var objectConverter = _toSpeckle.ResolveConverter(type);

    var rawGeometry = objectConverter.Convert(dbObject);

    tr.Commit();

    if (dbObject is not Entity entity)
    {
      return rawGeometry;
    }

    var (displayValue, rawEncoding) = DataObjectDisplayValueExtractor.Extract(rawGeometry);
    var properties = _propertiesExtractor.GetProperties(entity);
    string typeName = type.Name;

    return new AutocadObject
    {
      name = typeName,
      type = typeName,
      displayValue = displayValue,
      properties = properties,
      units = _settingsStore.Current.SpeckleUnits,
      rawEncoding = rawEncoding,
    };
  }
}
