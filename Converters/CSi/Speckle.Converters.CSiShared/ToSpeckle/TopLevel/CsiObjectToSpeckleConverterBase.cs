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
/// 2. PropertiesExtractor: Combines:
///    - General CSi properties (common across products)
///    - Product-specific class properties (through IClassPropertyExtractor)
///
/// The Convert method defines the template for conversion:
/// - Extracts display geometry
/// - Gathers properties
/// - Delegates final object creation to product-specific implementations
/// </remarks>
public abstract class CsiObjectToSpeckleConverter : IToSpeckleTopLevelConverter
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;
  private readonly DisplayValueExtractor _displayValueExtractor;
  private readonly PropertiesExtractor _propertiesExtractor;

  protected CsiObjectToSpeckleConverter(
    IConverterSettingsStore<CsiConversionSettings> settingsStore,
    DisplayValueExtractor displayValueExtractor,
    PropertiesExtractor propertiesExtractor
  )
  {
    _settingsStore = settingsStore;
    _displayValueExtractor = displayValueExtractor;
    _propertiesExtractor = propertiesExtractor;
  }

  public Base Convert(object target) => Convert((CsiWrapperBase)target);

  public Base Convert(CsiWrapperBase wrapper)
  {
    var displayValue = _displayValueExtractor.GetDisplayValue(wrapper).ToList();
    var properties = _propertiesExtractor.GetProperties(wrapper);

    return CreateTargetObject(
      wrapper.Name,
      wrapper.ObjectName,
      new List<ICsiObject>(),
      displayValue,
      properties,
      _settingsStore.Current.SpeckleUnits
    );
  }

  protected abstract Base CreateTargetObject(
    string name,
    string type,
    List<ICsiObject> elements,
    List<Base> displayValue,
    Dictionary<string, object?> properties,
    string units
  );
}
