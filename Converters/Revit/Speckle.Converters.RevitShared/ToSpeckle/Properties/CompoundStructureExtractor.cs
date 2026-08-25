using System.Globalization;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Converters.RevitShared.ToSpeckle.Properties;

/// <summary>
/// The layer buildup of a compound element type — every <see cref="DB.HostObjAttributes"/> subclass, so walls,
/// floors, roofs and ceilings alike. One record per layer: material name, layer function, thickness and units.
/// </summary>
/// <remarks>
/// Lives beside <c>Material Quantities</c> at the top of <c>properties</c>, not inside
/// <c>Parameters.Type Parameters</c> where <see cref="ParameterExtractor"/> used to build it [ENG-9338]. A compound
/// structure is not a Revit parameter; the old home routed it into the type-scoped eav table, readable only through
/// a join. Layers are keyed by ORDINAL in <c>GetLayers()</c> order (exterior → interior) — the former
/// <c>{material} ({layerId})</c> key carried no order, minted one eav path per material name, and broke the
/// dot-delimited paths on a name like "Insulation 2.5mm". A layer with no assigned material keeps its slot.
/// </remarks>
public class CompoundStructureExtractor
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _settingsStore;
  private readonly ScalingServiceToSpeckle _scalingServiceToSpeckle;

  // Keyed by the type's UniqueId, which is unique across documents — an ElementId is not, and this extractor is
  // scoped to the whole send operation: host document plus every linked model.
  private readonly Dictionary<string, Dictionary<string, object?>?> _structureCache = new();

  public CompoundStructureExtractor(
    IConverterSettingsStore<RevitConversionSettings> settingsStore,
    ScalingServiceToSpeckle scalingServiceToSpeckle
  )
  {
    _settingsStore = settingsStore;
    _scalingServiceToSpeckle = scalingServiceToSpeckle;
  }

  /// <summary>
  /// The layer buildup of <paramref name="element"/>'s type, or null when the type is not a compound element or
  /// carries no compound structure (a curtain wall, a generic model, an in-place family).
  /// </summary>
  public Dictionary<string, object?>? GetCompoundStructure(DB.Element element)
  {
    if (_settingsStore.Current.Document.GetElement(element.GetTypeId()) is not DB.HostObjAttributes type)
    {
      return null;
    }

    if (_structureCache.TryGetValue(type.UniqueId, out Dictionary<string, object?>? cached))
    {
      return cached;
    }

    // Cache the miss too: a curtain-wall type is a HostObjAttributes with no compound structure, and without a
    // negative entry every one of its elements pays another GetCompoundStructure call.
    if (type.GetCompoundStructure() is not DB.CompoundStructure structure) // GetCompoundStructure can return null
    {
      _structureCache[type.UniqueId] = null;
      return null;
    }

    var factor = _scalingServiceToSpeckle.ScaleLength(1);
    var layers = structure.GetLayers();
    var structureDictionary = new Dictionary<string, object?>();
    for (int i = 0; i < layers.Count; i++)
    {
      var layer = layers[i];
      structureDictionary[i.ToString(CultureInfo.InvariantCulture)] = new Dictionary<string, object?>()
      {
        ["material"] = (_settingsStore.Current.Document.GetElement(layer.MaterialId) as DB.Material)?.Name,
        ["function"] = layer.Function.ToString(),
        ["thickness"] = layer.Width * factor,
        ["units"] = _settingsStore.Current.SpeckleUnits,
      };
    }

    // A structure with no layers is not a buildup — don't put an empty dict on the object.
    var result = structureDictionary.Count > 0 ? structureDictionary : null;
    _structureCache[type.UniqueId] = result;
    return result;
  }
}
