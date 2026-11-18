using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Extensions;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Converters.RevitShared.ToSpeckle.Properties;
using Speckle.Sdk.Common.Exceptions;
using ApplicationException = Autodesk.Revit.Exceptions.ApplicationException;

namespace Speckle.Converters.RevitShared.ToSpeckle;

/// <summary>
/// Lighter converter for material quantities.
/// </summary>
/// <remarks>
/// We need to validate this with user needs. Currently limited to:
/// <list type="bullet">
///     <item><description>material category</description></item>
///     <item><description>material class</description></item>
///     <item><description>material name</description></item>
///     <item><description>area</description></item>
///     <item><description>volume</description></item>
///     <item><description>density (if valid StructuralAssetId)</description></item>
///     <item><description>type (if valid StructuralAssetId)</description></item>
///     <item><description>concrete compressive strength (if valid StructuralAssetId and of type concrete)</description></item>
/// </list>
/// We're attaching density, type and concrete compression (if concrete) to all objects. This is still "lite". If we add
/// more structural asset properties we should move to a proxy approach.
/// </remarks>
public class MaterialQuantitiesToSpeckleLite : ITypedConverter<DB.Element, Dictionary<string, object>>
{
  private readonly ScalingServiceToSpeckle _scalingService;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly StructuralMaterialAssetExtractor _structuralAssetExtractor;

  public MaterialQuantitiesToSpeckleLite(
    ScalingServiceToSpeckle scalingService,
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    StructuralMaterialAssetExtractor structuralAssetExtractor
  )
  {
    _scalingService = scalingService;
    _converterSettings = converterSettings;
    _structuralAssetExtractor = structuralAssetExtractor;
  }

  public Dictionary<string, object> Convert(DB.Element target)
  {
    Dictionary<string, object> quantities = new();
    switch (target)
    {
      // Rails carry no material on their category, so HasMaterialQuantities is false and ProcessMaterialsByCategory
      // finds nothing — the material is a TYPE parameter and the length an INSTANCE parameter, so they need the
      // by-element-type path instead [ENG-7793]. Each rail reports only ITS OWN length: a railing publishes its top
      // rail and handrails as separate child objects, so summing theirs into the parent as well would double-count
      // every railing in a takeoff [ENG-9358].
      case DBA.Railing railing:
        ProcessMaterialsByElementTypes([railing], quantities);
        break;

      case DBA.TopRail topRail:
        ProcessMaterialsByElementTypes([topRail], quantities);
        break;

      case DBA.HandRail handRail:
        ProcessMaterialsByElementTypes([handRail], quantities);
        break;

      case DBA.Stairs stairs:
        // stairs are container elements: GetMaterialVolume() on the stair itself returns 0
        // because the solid geometry belongs to its components (runs, landings, supports).
        // aggregate material quantities from the components instead (ENG-8733)
        ProcessStairMaterials(stairs, quantities);
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

  /// <summary>
  /// Extracts material quantities for stairs by aggregating over their components (runs, landings and supports),
  /// since the stair element itself reports no material volumes.
  /// </summary>
  /// <remarks>
  /// Areas and volumes of the same material are summed across components, matching what Revit shows in a
  /// material takeoff schedule. Falls back to category-based extraction if no component yields any material.
  /// </remarks>
  private void ProcessStairMaterials(DBA.Stairs stairs, Dictionary<string, object> quantities)
  {
    List<DB.ElementId> componentIds =
    [
      .. stairs.GetStairsRuns(),
      .. stairs.GetStairsLandings(),
      .. stairs.GetStairsSupports(),
    ];

    // sum area and volume per material across all stair components
    Dictionary<DB.ElementId, (double Area, double Volume)> materialTotals = new();
    foreach (DB.ElementId componentId in componentIds)
    {
      if (_converterSettings.Current.Document.GetElement(componentId) is not DB.Element component)
      {
        continue;
      }

      foreach (DB.ElementId? matId in component.GetMaterialIds(false))
      {
        if (matId is null)
        {
          continue;
        }

        try
        {
          double area = component.GetMaterialArea(matId, false);
          double volume = component.GetMaterialVolume(matId);
          materialTotals[matId] = materialTotals.TryGetValue(matId, out (double Area, double Volume) totals)
            ? (totals.Area + area, totals.Volume + volume)
            : (area, volume);
        }
        catch (ApplicationException ex)
        {
          throw new ConversionException("Error in Material Quantities", ex);
        }
      }
    }

    if (materialTotals.Count == 0)
    {
      // e.g. stairs without accessible components - fall back to the default behaviour
      ProcessMaterialsByCategory(stairs, quantities);
      return;
    }

    var unitSettings = _converterSettings.Current.Document.GetUnits();
    var areaUnitType = unitSettings.GetFormatOptions(DB.SpecTypeId.Area).GetUnitTypeId();
    var volumeUnitType = unitSettings.GetFormatOptions(DB.SpecTypeId.Volume).GetUnitTypeId();

    foreach (var entry in materialTotals)
    {
      var materialQuantity = new Dictionary<string, object>();
      if (TryAddMaterialPropertiesToQuantitiesDict(entry.Key, materialQuantity, out string matName))
      {
        quantities[matName] = materialQuantity;
        AddMaterialProperty(
          materialQuantity,
          "area",
          _scalingService.Scale(entry.Value.Area, areaUnitType),
          areaUnitType
        );
        AddMaterialProperty(
          materialQuantity,
          "volume",
          _scalingService.Scale(entry.Value.Volume, volumeUnitType),
          volumeUnitType
        );
      }
    }
  }

  /// <summary>
  /// The material a type points at. Prefers the declared <c>Reference.Material</c> spec, then falls back to any
  /// ElementId parameter that actually resolves to a <see cref="DB.Material"/> — rail types were reported not to
  /// match on the spec alone, and the fallback removes the need to know which [ENG-9358].
  /// </summary>
  private DB.ElementId FindMaterialParameterValue(DB.ElementType elementType)
  {
    DB.ElementId fallback = DB.ElementId.InvalidElementId;
    foreach (DB.Parameter param in elementType.Parameters)
    {
      if (param.StorageType != DB.StorageType.ElementId)
      {
        continue;
      }
      if (param.Definition.GetDataType() == DB.SpecTypeId.Reference.Material)
      {
        return param.AsElementId();
      }
      if (
        fallback == DB.ElementId.InvalidElementId
        && _converterSettings.Current.Document.GetElement(param.AsElementId()) is DB.Material
      )
      {
        fallback = param.AsElementId();
      }
    }
    return fallback;
  }

  /// <summary>
  /// An element's own length. The curve-driven built-in parameter first — that is the "Length" a rail reports and the
  /// value a takeoff expects — falling back to summing every length-spec parameter, which is what this path did
  /// before and stays the behaviour for anything without a driving curve.
  /// </summary>
  private static double GetElementLength(DB.Element element)
  {
    if (element.get_Parameter(DB.BuiltInParameter.CURVE_ELEM_LENGTH) is DB.Parameter curveLength)
    {
      return curveLength.AsDouble();
    }

    double total = 0;
    foreach (DB.Parameter param in element.Parameters)
    {
      if (param.Definition.GetDataType() == DB.SpecTypeId.Length)
      {
        total += param.AsDouble();
      }
    }
    return total;
  }

  /// <summary>
  /// Length-based quantities for elements whose material lives on their TYPE and whose length lives on the INSTANCE —
  /// rails, which have no category material quantities at all.
  /// </summary>
  /// <remarks>
  /// Takes INSTANCES. It used to take element ids and call <c>GetTypeId()</c> on whatever they resolved to, but the
  /// callers passed TYPE ids: <c>ElementType.GetTypeId()</c> is <c>InvalidElementId</c>, so every entry was skipped
  /// and this path emitted nothing on any file since it was written [ENG-9358]. A type id could not have worked
  /// anyway — the length is an instance parameter.
  /// </remarks>
  private void ProcessMaterialsByElementTypes(IReadOnlyList<DB.Element> elements, Dictionary<string, object> quantities)
  {
    Dictionary<DB.ElementId, double> matLengths = new(); // stores mat id to total length found for mat

    foreach (DB.Element element in elements)
    {
      if (_converterSettings.Current.Document.GetElement(element.GetTypeId()) is not DB.ElementType elementType)
      {
        continue;
      }

      DB.ElementId elementMatId = FindMaterialParameterValue(elementType);
      if (elementMatId == DB.ElementId.InvalidElementId)
      {
        continue;
      }

      double length = GetElementLength(element);
      if (length == 0)
      {
        continue;
      }

      if (matLengths.TryGetValue(elementMatId, out double _))
      {
        matLengths[elementMatId] += length;
      }
      else
      {
        matLengths.Add(elementMatId, length);
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
  /// Adds the material properties (like name, category, and class) to the material quantity dictionary
  /// </summary>
  /// <param name="matId">the material id</param>
  /// <param name="materialQuantity"></param>
  /// <param name="matName"></param>
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
      // No API to identify light-cone materials by ID; exclude by well-known default name.
      if (material.Name == "Default Light Source")
      {
        return false;
      }
      materialQuantity["materialName"] = material.Name;
      materialQuantity["materialCategory"] = material.MaterialCategory;
      materialQuantity["materialClass"] = material.MaterialClass;

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
    var property = new Dictionary<string, object> { ["name"] = name, ["value"] = value };

    // language-stable unit identifier (e.g. "squareMeters") instead of the localized display label
    // (e.g. "Quadratmeter" in German Revit), so downstream tooling can resolve units. See ENG-8735.
    if (unitId.GetStableUnitsId() is string units)
    {
      property["units"] = units;
    }

    materialQuantity[name] = property;
  }
}
