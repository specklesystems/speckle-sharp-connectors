using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Converters.ETABSShared.ToSpeckle.Helpers;

/// <summary>
/// Attempts to resolve the section type and retrieve its properties by trying different section resolvers.
/// </summary>
/// <remarks>
/// This service focuses solely on determining the correct section type and returning its properties.
/// Since section names are unique across different types (Wall, Slab, Deck), it uses a try-and-fail approach
/// rather than attempting to predetermine the type. The first successful resolution is returned.
/// </remarks>
public record AreaSectionResult
{
  public bool Success { get; init; }
  public Dictionary<string, object?> Properties { get; init; }
}

public interface IAreaSectionResolver
{
  AreaSectionResult TryResolveSection(string sectionName);
}

public class EtabsShellSectionResolver
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;
  private readonly IEnumerable<IAreaSectionResolver> _resolvers;

  public EtabsShellSectionResolver(IConverterSettingsStore<CsiConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
    _resolvers =
    [
      new WallSectionResolver(_settingsStore),
      new SlabSectionResolver(_settingsStore),
      new DeckSectionResolver(_settingsStore)
    ];
  }

  public Dictionary<String, object?> ResolveSection(string sectionName)
  {
    foreach (var resolver in _resolvers)
    {
      var result = resolver.TryResolveSection(sectionName);
      if (result.Success)
      {
        return result.Properties;
      }
    }

    throw new InvalidOperationException($"Section '{sectionName}' could not be resolved to any known type.");
  }
}

public class WallSectionResolver(IConverterSettingsStore<CsiConversionSettings> settingsStore) : IAreaSectionResolver
{
  public AreaSectionResult TryResolveSection(string sectionName)
  {
    eWallPropType wallPropType = default;
    eShellType shellType = default;
    string matProp = string.Empty;
    double thickness = 0.0;
    int color = 0;
    string notes = string.Empty;
    string guid = string.Empty;

    var result = settingsStore.Current.SapModel.PropArea.GetWall(
      sectionName,
      ref wallPropType,
      ref shellType,
      ref matProp,
      ref thickness,
      ref color,
      ref notes,
      ref guid
    );

    Dictionary<string, object?> generalData = [];
    generalData["Property Name"] = sectionName;
    generalData["Property Type"] = wallPropType.ToString();
    generalData["Material"] = matProp;
    generalData["Modeling Type"] = shellType.ToString();
    generalData["Display Color"] = color;
    generalData["Notes"] = notes;

    Dictionary<string, object?> propertyData = [];
    propertyData["Type"] = "Wall";
    propertyData.AddWithUnits("Thickness", thickness, settingsStore.Current.SpeckleUnits);

    Dictionary<string, object?> properties = [];
    properties[SectionPropertyCategory.GENERAL_DATA] = generalData;
    properties[SectionPropertyCategory.PROPERTY_DATA] = propertyData;

    return new AreaSectionResult { Success = result == 0, Properties = properties };
  }
}

public class SlabSectionResolver(IConverterSettingsStore<CsiConversionSettings> settingsStore) : IAreaSectionResolver
{
  public AreaSectionResult TryResolveSection(string sectionName)
  {
    eSlabType slabType = default;
    eShellType shellType = default;
    string matProp = string.Empty;
    double thickness = 0.0;
    int color = 0;
    string notes = string.Empty;
    string guid = string.Empty;

    var result = settingsStore.Current.SapModel.PropArea.GetSlab(
      sectionName,
      ref slabType,
      ref shellType,
      ref matProp,
      ref thickness,
      ref color,
      ref notes,
      ref guid
    );

    Dictionary<string, object?> generalData = [];
    generalData["Property Name"] = sectionName;
    generalData["Material"] = matProp;
    generalData["Modeling Type"] = shellType.ToString();
    generalData["Display Color"] = color;
    generalData["Notes"] = notes;

    Dictionary<string, object?> propertyData = [];
    propertyData["Type"] = slabType.ToString();
    propertyData.AddWithUnits("Thickness", thickness, settingsStore.Current.SpeckleUnits);

    Dictionary<string, object?> properties = [];
    properties[SectionPropertyCategory.GENERAL_DATA] = generalData;
    properties[SectionPropertyCategory.PROPERTY_DATA] = propertyData;

    return new AreaSectionResult { Success = result == 0, Properties = properties };
  }
}

public class DeckSectionResolver(IConverterSettingsStore<CsiConversionSettings> settingsStore) : IAreaSectionResolver
{
  public AreaSectionResult TryResolveSection(string sectionName)
  {
    eDeckType deckType = default;
    eShellType shellType = default;
    string deckMatProp = string.Empty;
    double thickness = 0.0;
    int color = 0;
    string notes = string.Empty;
    string guid = string.Empty;

    var result = settingsStore.Current.SapModel.PropArea.GetDeck(
      sectionName,
      ref deckType,
      ref shellType,
      ref deckMatProp,
      ref thickness,
      ref color,
      ref notes,
      ref guid
    );

    Dictionary<string, object?> generalData = [];
    generalData["Property Name"] = sectionName;
    generalData["Property Type"] = deckType.ToString();
    generalData["Material"] = deckMatProp;
    generalData["Modeling Type"] = shellType.ToString();
    generalData["Display Color"] = color;
    generalData["Notes"] = notes;

    Dictionary<string, object?> propertyData = [];
    propertyData.AddWithUnits("Thickness", thickness, settingsStore.Current.SpeckleUnits);

    Dictionary<string, object?> properties = [];
    properties[SectionPropertyCategory.GENERAL_DATA] = generalData;
    properties[SectionPropertyCategory.PROPERTY_DATA] = propertyData;

    return new AreaSectionResult { Success = result == 0, Properties = properties };
  }
}
