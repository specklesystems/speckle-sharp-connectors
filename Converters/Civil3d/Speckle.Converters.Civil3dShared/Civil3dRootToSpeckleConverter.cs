using Autodesk.AutoCAD.DatabaseServices;
using Speckle.Converters.Civil3dShared.ToSpeckle;
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
  private readonly PartDataExtractor _partDataExtractor;

  public Civil3dRootToSpeckleConverter(
    IConverterManager<IToSpeckleTopLevelConverter> toSpeckle,
    IConverterSettingsStore<Civil3dConversionSettings> settingsStore,
    PartDataExtractor partDataExtractor
  )
  {
    _toSpeckle = toSpeckle;
    _settingsStore = settingsStore;
    _partDataExtractor = partDataExtractor;
  }

  public Base Convert(object target)
  {
    if (target is not DBObject dbObject)
    {
      throw new SpeckleConversionException(
        $"Conversion of {target.GetType().Name} to Speckle is not supported. Only objects that inherit from DBObject are."
      );
    }

    Type type = dbObject.GetType();
    object objectToConvert = dbObject;
    Dictionary<string, object?> properties = new();

    // check first for civil type objects

    if (target is CDB.Entity civilEntity)
    {
      type = civilEntity.GetType();
      objectToConvert = civilEntity;

      try
      {
        List<Dictionary<string, object?>>? partData = _partDataExtractor.GetPartData(civilEntity);
        if (partData is not null)
        {
          properties.Add("Part Data", partData);
        }
      }
      catch (Exception e) when (!e.IsFatal())
      {
        //TODO: logger here
      }
    }

    var objectConverter = _toSpeckle.ResolveConverter(type, true);

    if (objectConverter == null)
    {
      throw new SpeckleConversionException($"No conversion found for {target.GetType().Name}");
    }

    try
    {
      using (var l = _settingsStore.Current.Document.LockDocument())
      {
        using (var tr = _settingsStore.Current.Document.Database.TransactionManager.StartTransaction())
        {
          var result = objectConverter.Convert(objectToConvert);

          if (properties.Count > 0)
          {
            result["properties"] = properties;
          }

          tr.Commit();
          return result;
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
