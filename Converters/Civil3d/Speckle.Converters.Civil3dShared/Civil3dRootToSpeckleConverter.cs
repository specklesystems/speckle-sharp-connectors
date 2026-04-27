using System.ComponentModel.DataAnnotations;
using Speckle.Converters.AutocadShared.ToSpeckle;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Objects.Data;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Civil3dShared;

public class Civil3dRootToSpeckleConverter : IRootToSpeckleConverter
{
  private readonly IConverterManager<IToSpeckleTopLevelConverter> _toSpeckle;
  private readonly IConverterSettingsStore<Civil3dConversionSettings> _settingsStore;
  private readonly ToSpeckle.PropertiesExtractor _propertiesExtractor;

  public Civil3dRootToSpeckleConverter(
    IConverterManager<IToSpeckleTopLevelConverter> toSpeckle,
    IConverterSettingsStore<Civil3dConversionSettings> settingsStore,
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

    if (target is CDB.AlignmentLabelGroup) // TODO: this should not throw and be reported from connector instead, similar to supported categories in Revit.
    {
      throw new ValidationException($"Conversion of {target.GetType().Name} to Speckle is not supported yet.");
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

    using var l = _settingsStore.Current.Document.LockDocument();
    using var tr = _settingsStore.Current.Document.Database.TransactionManager.StartTransaction();
    var result = objectConverter.Convert(target);

    tr.Commit();

    // Civil entity converters already return Civil3dObject. Only wrap raw autocad entities
    // (eg plain ADB.Line, ADB.Arc via the autocad top-level converters) into an AutocadObject.
    // If a Civil3d-rank top-level converter (eg Civil3d's Solid3dToSpeckleConverter) already
    // produced a DataObject for an ADB.Entity target, pass through without re-wrapping.
    if (target is not CDB.Entity && target is ADB.Entity autocadEntity && result is not DataObject)
    {
      var (displayValue, rawEncoding) = DataObjectDisplayValueExtractor.Extract(result);
      var properties = _propertiesExtractor.GetProperties(autocadEntity);
      string typeName = autocadEntity.GetType().Name;

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

    return result;
  }
}
