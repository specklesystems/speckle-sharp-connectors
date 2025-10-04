using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Converters.ToSpeckle.Properties;
using Speckle.Sdk.Common.Exceptions;
using ApplicationException = Autodesk.Revit.Exceptions.ApplicationException;

namespace Speckle.Converters.RevitShared.ToSpeckle;

/// <summary>
/// Lighter converter for material quantities.
/// </summary>
/// <remarks>
/// We need to validate this with user needs. Was limited to basic volume and instance-based numeric quantities. However,
/// this has grown to include material (not instance-based) strings for Fast+Epp, now custom parameters for Stantec.
/// Approach is now inefficient. We need a proxy / detached props approach that satisfies all workflows (bi, viewer, etc.)
/// </remarks>
public class MaterialQuantitiesToSpeckleLite : ITypedConverter<DB.Element, Dictionary<string, object>>
{
  private readonly ScalingServiceToSpeckle _scalingService;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly StructuralMaterialAssetExtractor _structuralAssetExtractor;
  private readonly CustomMaterialParametersExtractor _customMaterialParametersExtractor;

  public MaterialQuantitiesToSpeckleLite(
    ScalingServiceToSpeckle scalingService,
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    StructuralMaterialAssetExtractor structuralAssetExtractor,
    CustomMaterialParametersExtractor customMaterialParametersExtractor
  )
  {
    _scalingService = scalingService;
    _converterSettings = converterSettings;
    _structuralAssetExtractor = structuralAssetExtractor;
    _customMaterialParametersExtractor = customMaterialParametersExtractor;
  }

  public Dictionary<string, object> Convert(DB.Element target)
  {
    Dictionary<string, object> quantities = new();
    switch (target)
    {
      case DBA.Railing railing:
        // railings can have subelements including top rails, hand rails, and balusters.
        // they also do *not* have any materials associated with their category.
        List<DB.ElementId> railingElementIds = [railing.GetTypeId(), railing.TopRail, .. railing.GetHandRails()];
        ProcessMaterialsByElementTypes(railingElementIds, quantities);
        break;
      default:
        ProcessMaterialsByCategory(target, quantities);
        break;
    }

    return quantities;
  }

  private void ProcessMaterialsByCategory(DB.Element element, Dictionary<string, object> quantities)
  {
    if (element.Category?.HasMaterialQuantities ?? false) //category can be null
    {
      foreach (DB.ElementId? matId in element.GetMaterialIds(false))
      {
        if (matId is null)
        {
          continue;
        }

        var materialQuantity = new Dictionary<string, object>();
        var unitSettings = _converterSettings.Current.Document.GetUnits();

        // add material props
        if (TryAddMaterialPropertiesToQuantitiesDict(matId, materialQuantity, out string matName))
        {
          quantities[matName] = materialQuantity;
        }

        try
        {
          // add area and volume props
          var areaUnitType = unitSettings.GetFormatOptions(DB.SpecTypeId.Area).GetUnitTypeId();
          AddMaterialProperty(
            materialQuantity,
            "area",
            _scalingService.Scale(element.GetMaterialArea(matId, false), areaUnitType),
            areaUnitType
          );

          var volumeUnitType = unitSettings.GetFormatOptions(DB.SpecTypeId.Volume).GetUnitTypeId();
          AddMaterialProperty(
            materialQuantity,
            "volume",
            _scalingService.Scale(element.GetMaterialVolume(matId), volumeUnitType),
            volumeUnitType
          );
        }
        catch (ApplicationException ex)
        {
          throw new ConversionException("Error in Material Quantities", ex);
        }
      }
    }
  }

  private void ProcessMaterialsByElementTypes(List<DB.ElementId> elementIds, Dictionary<string, object> quantities)
  {
    Dictionary<DB.ElementId, double> matLengths = new(); // stores mat id to total length found for mat

    foreach (DB.ElementId elementId in elementIds)
    {
      if (
        _converterSettings.Current.Document.GetElement(elementId) is DB.Element element
        && _converterSettings.Current.Document.GetElement(element.GetTypeId()) is DB.ElementType elementType
      )
      {
        DB.ElementId elementMatId = DB.ElementId.InvalidElementId;

        foreach (DB.Parameter param in elementType.Parameters)
        {
          DB.Definition def = param.Definition;
          if (param.StorageType == DB.StorageType.ElementId && def.GetDataType() == DB.SpecTypeId.Reference.Material)
          {
            elementMatId = param.AsElementId();
            break;
          }
        }

        if (elementMatId != DB.ElementId.InvalidElementId)
        {
          // try get the length from the element
          foreach (DB.Parameter eParam in element.Parameters)
          {
            DB.Definition eParamDef = eParam.Definition;
            var forgeTypeId = eParamDef.GetDataType();
            if (forgeTypeId == DB.SpecTypeId.Length)
            {
              double length = eParam.AsDouble();
              if (matLengths.TryGetValue(elementMatId, out double _))
              {
                matLengths[elementMatId] += length;
              }
              else
              {
                matLengths.Add(elementMatId, length);
              }
            }
          }
        }
      }
    }

    foreach (var entry in matLengths)
    {
      var materialQuantity = new Dictionary<string, object>();
      var unitSettings = _converterSettings.Current.Document.GetUnits();

      // add material props
      if (TryAddMaterialPropertiesToQuantitiesDict(entry.Key, materialQuantity, out string matName))
      {
        quantities[matName] = materialQuantity;

        // add length prop
        var lengthUnitType = unitSettings.GetFormatOptions(DB.SpecTypeId.Length).GetUnitTypeId();
        AddMaterialProperty(
          materialQuantity,
          "length",
          _scalingService.Scale(entry.Value, lengthUnitType),
          lengthUnitType
        );
      }
    }
  }

  /// <summary>
  /// Adds the material properties to the material quantity dictionary
  /// </summary>
  /// <returns>true if material is found, false if not</returns>
  private bool TryAddMaterialPropertiesToQuantitiesDict(
    DB.ElementId matId,
    Dictionary<string, object> materialQuantity,
    out string matName
  )
  {
    matName = "";
    if (_converterSettings.Current.Document.GetElement(matId) is DB.Material material)
    {
      materialQuantity["materialName"] = material.Name;
      materialQuantity["materialCategory"] = material.MaterialCategory;
      materialQuantity["materialClass"] = material.MaterialClass;

      // NOTE: this is inefficient, we're slapping some props to multiple instances
      // nothing would make me happier if we can get around this. Until we have a better properties approach, this serves
      // as an INTERIM (hopefully) approach
      if (_converterSettings.Current.SendMaterialCustomParameters)
      {
        var customParams = _customMaterialParametersExtractor.TryGetCustomParameters(matId);
        if (customParams.Count > 0)
        {
          materialQuantity["Custom Parameters"] = customParams;
        }
      }

      // get StructuralAssetId (or try to)
      DB.ElementId structuralAssetId = material.StructuralAssetId;
      if (structuralAssetId != DB.ElementId.InvalidElementId)
      {
        StructuralAssetProperties structuralAssetProperties = _structuralAssetExtractor.TryGetProperties(
          structuralAssetId
        );

        materialQuantity["structuralAsset"] = structuralAssetProperties.Name;
        AddMaterialProperty(
          materialQuantity,
          "density",
          structuralAssetProperties.Density,
          structuralAssetProperties.DensityUnitId
        );

        // more reliable way of determining material type (wood/concrete/type) as it uses Revit enum
        // materialClass, materialCategory etc. are user string inputs
        materialQuantity["materialType"] = structuralAssetProperties.MaterialType;

        // Only add compressive strength for concrete materials (used by F+E for Automate)
        if (
          structuralAssetProperties.MaterialType == "Concrete"
          && structuralAssetProperties.CompressiveStrength.HasValue
        )
        {
          AddMaterialProperty(
            materialQuantity,
            "compressiveStrength",
            structuralAssetProperties.CompressiveStrength.Value,
            structuralAssetProperties.CompressiveStrengthUnitId!
          );
        }
      }

      matName = material.Name;
      return true;
    }

    return false;
  }

  /// <summary>
  /// Adds a material property to the given dictionary with standardized structure.
  /// </summary>
  /// <param name="materialQuantity">The dictionary to mutate with the new property</param>
  /// <param name="name">The name of the property (e.g., "area", "volume", "density")</param>
  /// <param name="value">The numeric value of the property</param>
  /// <param name="unitId">The Forge type ID representing the units of the property</param>
  /// <remarks>
  /// Saves code when used repeatedly. Etabs implements an extension method to dicts (see utils folder). May be worth exploring.
  /// </remarks>
  private void AddMaterialProperty(
    Dictionary<string, object> materialQuantity,
    string name,
    double value,
    DB.ForgeTypeId unitId
  )
  {
    materialQuantity[name] = new Dictionary<string, object>
    {
      ["name"] = name,
      ["value"] = value,
      ["units"] = DB.LabelUtils.GetLabelForUnit(unitId)
    };
  }
}
