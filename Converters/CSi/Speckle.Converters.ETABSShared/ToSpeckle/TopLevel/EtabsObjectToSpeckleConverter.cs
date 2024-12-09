using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.ToSpeckle.Helpers;
using Speckle.Converters.CSiShared.ToSpeckle.TopLevel;
using Speckle.Objects;
using Speckle.Objects.Data;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ETABSShared.ToSpeckle.TopLevel;

/// <summary>
/// Top level converter responsible for converting Etabs objects to Speckle objects.
/// Implements the Template Method pattern through inheritance from CsiObjectToSpeckleConverterBase.
/// </summary>
/// <remarks>
/// Conversion Flow:
/// 1. EtabsObjectToSpeckleConverter inherits base conversion logic from CsiObjectToSpeckleConverterBase
/// 2. Base Convert method orchestrates the conversion process:
///    - DisplayValue extraction (handled by CsiShared - shared geometry conversion)
///    - Property extraction (combination of CsiShared and EtabsShared)
///      * CsiGeneralPropertiesExtractor for common CSi properties
///      * EtabsClassPropertiesExtractor (implements IClassPropertyExtractor) for ETABS-specific properties
/// 3. CreateTargetObject method ensures type-safe conversion to EtabsObject
/// </remarks>
[NameAndRankValue(nameof(CsiWrapperBase), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class EtabsObjectToSpeckleConverter : CsiObjectToSpeckleConverterBase
{
  public EtabsObjectToSpeckleConverter(
    IConverterSettingsStore<CsiConversionSettings> settingsStore,
    DisplayValueExtractor displayValueExtractor,
    PropertiesExtractor propertiesExtractor
  )
    : base(settingsStore, displayValueExtractor, propertiesExtractor) { }

  protected override Base CreateTargetObject(
    string name,
    string type,
    List<ICsiObject> elements,
    List<Base> displayValue,
    Dictionary<string, object?> properties,
    string units
  ) =>
    new EtabsObject
    {
      name = name,
      type = type,
      elements = elements.Cast<EtabsObject>().ToList(),
      displayValue = displayValue,
      properties = properties,
      units = units
    };
}
