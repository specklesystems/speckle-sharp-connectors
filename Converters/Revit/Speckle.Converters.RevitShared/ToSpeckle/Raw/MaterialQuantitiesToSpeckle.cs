using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Services;
using Speckle.Objects.Other;
using Speckle.Objects.Other.Revit;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class MaterialQuantitiesToSpeckle : ITypedConverter<DB.Element, IEnumerable<MaterialQuantity>>
{
  private readonly ITypedConverter<DB.Material, RevitMaterial> _materialConverter;
  private readonly DisplayValueExtractor _displayValueExtractor;
  private readonly ScalingServiceToSpeckle _scalingService;
  private readonly IRevitConversionContextStack _contextStack;

  public MaterialQuantitiesToSpeckle(
    ITypedConverter<DB.Material, RevitMaterial> materialConverter,
    DisplayValueExtractor displayValueExtractor,
    ScalingServiceToSpeckle scalingService,
    IRevitConversionContextStack contextStack
  )
  {
    _materialConverter = materialConverter;
    _displayValueExtractor = displayValueExtractor;
    _scalingService = scalingService;
    _contextStack = contextStack;
  }

  /// <summary>
  /// Material Quantities in Revit are stored in different ways and therefore need to be retrieved
  /// using different methods. According to this forum post https://forums.autodesk.com/t5/revit-api-forum/method-getmaterialarea-appears-to-use-different-formulas-for/td-p/11988215
  /// "Hosts" will return the area of a single side of the object and non-host objects will return the combined area of every side of the element.
  /// Certain MEP element materials are attached to the MEP system that the element belongs to.
  /// </summary>
  /// <param name="target"></param>
  /// <returns></returns>
  public IEnumerable<MaterialQuantity> Convert(DB.Element target)
  {
    switch (target)
    {
      // These elements will report a single face from the material area api call
      case DB.CeilingAndFloor:
      case DB.Wall:
      case DB.RoofBase:
        return GetMaterialQuantitiesFromAPICall(target);

      // These are MEP elements where material quantities are attached to their MEP system
      case DB.Mechanical.Duct:
      case DB.Mechanical.FlexDuct:
      case DB.Plumbing.Pipe:
      case DB.Plumbing.FlexPipe:
        MaterialQuantity? quantity = GetMaterialQuantityForMEPElement(target);
        return quantity == null ? Enumerable.Empty<MaterialQuantity>() : new List<MaterialQuantity>() { quantity };

      default:
        return GetMaterialQuantitiesFromSolids(target);
    }
  }

  private IEnumerable<MaterialQuantity> GetMaterialQuantitiesFromAPICall(DB.Element element)
  {
    foreach (DB.ElementId matId in element.GetMaterialIds(false))
    {
      double volume = element.GetMaterialVolume(matId);
      double area = element.GetMaterialArea(matId, false);
      yield return CreateMaterialQuantity(element, matId, area, volume);
    }
  }

  private MaterialQuantity? GetMaterialQuantityForMEPElement(DB.Element element)
  {
    DB.Material? material = GetMEPSystemRevitMaterial(element);
    if (material == null)
    {
      return null;
    }

    var (solids, _) = _displayValueExtractor.GetSolidsAndMeshesFromElement(element, null);

    (double area, double volume) = GetAreaAndVolumeFromSolids(solids);
    return CreateMaterialQuantity(element, material.Id, area, volume);
  }

  //Retrieves the revit material from assigned system type for mep elements
  private static DB.Material? GetMEPSystemRevitMaterial(DB.Element e)
  {
    DB.ElementId idType = DB.ElementId.InvalidElementId;

    if (e is DB.MEPCurve dt)
    {
      idType = dt.MEPSystem.GetTypeId();
    }

    if (idType == DB.ElementId.InvalidElementId)
    {
      return null;
    }

    if (e.Document.GetElement(idType) is DB.MEPSystemType mechType)
    {
      return e.Document.GetElement(mechType.MaterialId) as DB.Material;
    }

    return null;
  }

  private IEnumerable<MaterialQuantity> GetMaterialQuantitiesFromSolids(DB.Element element)
  {
    var (solids, _) = _displayValueExtractor.GetSolidsAndMeshesFromElement(element, null);

    var solidMaterials = solids
      .Where(solid => solid.Volume > 0 && !solid.Faces.IsEmpty)
      .Select(m => m.Faces.get_Item(0).MaterialElementId)
      .Distinct();

    foreach (DB.ElementId matId in solidMaterials)
    {
      (double area, double volume) = GetAreaAndVolumeFromSolids(solids, matId);
      yield return CreateMaterialQuantity(element, matId, area, volume);
    }
  }

  private (double, double) GetAreaAndVolumeFromSolids(List<DB.Solid> solids, DB.ElementId? materialId = null)
  {
    if (materialId != null)
    {
      solids = solids
        .Where(solid =>
          solid.Volume > 0 && !solid.Faces.IsEmpty && solid.Faces.get_Item(0).MaterialElementId == materialId
        )
        .ToList();
    }

    double volume = solids.Sum(solid => solid.Volume);
    IEnumerable<double> areaOfLargestFaceInEachSolid = solids.Select(solid =>
      solid.Faces.Cast<DB.Face>().Select(face => face.Area).Max()
    );
    double area = areaOfLargestFaceInEachSolid.Sum();
    return (area, volume);
  }

  private MaterialQuantity CreateMaterialQuantity(
    DB.Element element,
    DB.ElementId materialId,
    double areaRevitInternalUnits,
    double volumeRevitInternalUnits
  )
  {
    // convert material
    if (_contextStack.Current.Document.GetElement(materialId) is DB.Material material)
    {
      RevitMaterial speckleMaterial = _materialConverter.Convert(material);
      double factor = _scalingService.ScaleLength(1);
      double area = factor * factor * areaRevitInternalUnits;
      double volume = factor * factor * factor * volumeRevitInternalUnits;

      MaterialQuantity materialQuantity = new(speckleMaterial, volume, area, _contextStack.Current.SpeckleUnits);

      switch (element)
      {
        case DB.Architecture.Railing railing:
          materialQuantity["length"] = railing.GetPath().Sum(e => e.Length) * factor;
          break;

        case DB.Architecture.ContinuousRail continuousRail:
          materialQuantity["length"] = continuousRail.GetPath().Sum(e => e.Length) * factor;
          break;

        default:
          if (element.Location is DB.LocationCurve curve)
          {
            materialQuantity["length"] = curve.Curve.Length * factor;
          }
          break;
      }

      return materialQuantity;
    }
    else
    {
      throw new SpeckleConversionException("Could not retrieve material from document");
    }
  }
}
