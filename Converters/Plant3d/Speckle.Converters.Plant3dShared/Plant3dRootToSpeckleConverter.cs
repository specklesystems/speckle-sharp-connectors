using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Objects.Data;
using Speckle.Sdk;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Plant3dShared;

public class Plant3dRootToSpeckleConverter(
  IConverterManager<IToSpeckleTopLevelConverter> toSpeckle,
  IConverterSettingsStore<Plant3dConversionSettings> settingsStore,
  ToSpeckle.PropertiesExtractor propertiesExtractor,
  ToSpeckle.Plant3dDataExtractor dataExtractor,
  ILogger<Plant3dRootToSpeckleConverter> logger
) : IRootToSpeckleConverter
{
  public Base Convert(object target)
  {
    if (target is not ADB.DBObject dbObject)
    {
      throw new ValidationException(
        $"Conversion of {target.GetType().Name} to Speckle is not supported. Only objects that inherit from DBObject are."
      );
    }

    Type type = dbObject.GetType();

    var objectConverter = toSpeckle.ResolveConverter(type);

    using var l = settingsStore.Current.Document.LockDocument();
    using var tr = settingsStore.Current.Document.Database.TransactionManager.StartTransaction();
    Base result = objectConverter.Convert(target);

    if (target is ADB.Entity autocadEntity)
    {
      // Property extraction is best-effort: a failure here must not discard already-converted geometry.
      // Extract AEC property sets and extension dictionaries
      Dictionary<string, object?> properties = new();
      try
      {
        properties = propertiesExtractor.GetProperties(autocadEntity);
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        logger.LogWarning(ex, "Failed to extract AEC properties on object {HandleValue}", autocadEntity.Handle.Value);
      }

      // Extract Plant3D project database properties (Tag, NominalDiameter, etc.)
      Dictionary<string, object?> dataProperties = new();
      try
      {
        dataProperties = dataExtractor.GetDataProperties(autocadEntity);
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        logger.LogWarning(ex, "Failed to extract P&ID properties on object {HandleValue}", autocadEntity.Handle.Value);
      }

      if (result is DataObject dataObject)
      {
        // Merge AEC properties
        foreach (var kvp in properties)
        {
          dataObject.properties[kvp.Key] = kvp.Value;
        }

        // Merge Plant3D data under "P&ID" key
        if (dataProperties.Count > 0)
        {
          dataObject.properties["P&ID"] = dataProperties;
        }
      }
      else
      {
        if (properties.Count > 0)
        {
          result["properties"] = properties;
        }
        if (dataProperties.Count > 0)
        {
          result["Plant3D Data"] = dataProperties;
        }
      }
    }

    tr.Commit();
    return result;
  }
}
