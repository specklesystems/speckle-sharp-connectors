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
/// Implements the Template Method pattern through inheritance from (abstract) CsiObjectToSpeckleConverterBase.
/// </summary>
/// <remarks>
/// Conversion Flow:
/// 1. EtabsObjectToSpeckleConverter inherits base conversion logic from CsiObjectToSpeckleConverterBase
/// 2. Base Convert method orchestrates the conversion process:
///    - DisplayValue extraction (handled by CsiShared - shared geometry conversion)
///    - Object data querying (combination of shared and application-specific data)
///      * SharedPropertiesExtractor for common CSi data
///      * IApplicationPropertiesExtractor for ETABS-specific data
/// 3. CreateTargetObject method ensures type-safe conversion to EtabsObject
/// </remarks>
[NameAndRankValue(typeof(CsiWrapperBase), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class EtabsObjectToSpeckleConverter : CsiObjectToSpeckleConverterBase
{
  public EtabsObjectToSpeckleConverter(
    IConverterSettingsStore<CsiConversionSettings> settingsStore,
    DisplayValueExtractor displayValueExtractor,
    IApplicationPropertiesExtractor applicationPropertiesExtractor
  )
    : base(settingsStore, displayValueExtractor, applicationPropertiesExtractor) { }

  protected override Base CreateTargetObject(
    string name,
    string type,
    List<ICsiObject> elements,
    List<Base> displayValue,
    Dictionary<string, object?> properties,
    string units,
    string applicationId
  ) =>
    new EtabsObject
    {
      name = name,
      type = type,
      elements = elements.Cast<EtabsObject>().ToList(),
      displayValue = displayValue,
      properties = properties,
      units = units,
      applicationId = applicationId
    };
}
