using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.CSiShared.ToSpeckle.Helpers;
using Speckle.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.CSiShared.ToSpeckle.TopLevel;

/// <summary>
/// Abstract base converter that serves as the foundation for product-specific CSi converters (ETABS, SAP2000).
/// Implements a Template Method pattern for object conversion while allowing product-specific customization.
/// </summary>
/// <remarks>
/// Core Components:
/// 1. DisplayValueExtractor: Handles geometry conversion common to all CSi products
/// 2. IApplicationPropertiesExtractor: Handles both shared and product-specific properties through composition
///
/// The Convert method defines the template for conversion:
/// - Extracts display geometry
/// - Gathers properties through the application-specific implementation
/// - Delegates final object creation to product-specific implementations
/// </remarks>
public abstract class CsiObjectToSpeckleConverterBase : IToSpeckleTopLevelConverter
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;
  private readonly DisplayValueExtractor _displayValueExtractor;
  private readonly IApplicationPropertiesExtractor _applicationPropertiesExtractor;

  protected CsiObjectToSpeckleConverterBase(
    IConverterSettingsStore<CsiConversionSettings> settingsStore,
    DisplayValueExtractor displayValueExtractor,
    IApplicationPropertiesExtractor applicationPropertiesExtractor
  )
  {
    _settingsStore = settingsStore;
    _displayValueExtractor = displayValueExtractor;
    _applicationPropertiesExtractor = applicationPropertiesExtractor;
  }

  public Base Convert(object target) => Convert((CsiWrapperBase)target);

  private Base Convert(CsiWrapperBase wrapper)
  {
    var displayValue = _displayValueExtractor.GetDisplayValue(wrapper).ToList();
    var objectData = _applicationPropertiesExtractor.ExtractProperties(wrapper);

    var baseObject = CreateTargetObject(
      objectData.Name,
      objectData.Type,
      new List<ICsiObject>(),
      displayValue,
      objectData.Properties,
      _settingsStore.Current.SpeckleUnits,
      objectData.ApplicationId
    );

    return baseObject;
  }

  protected abstract Base CreateTargetObject(
    string name,
    string type,
    List<ICsiObject> elements,
    List<Base> displayValue,
    Dictionary<string, object?> properties,
    string units,
    string applicationId
  );
}
