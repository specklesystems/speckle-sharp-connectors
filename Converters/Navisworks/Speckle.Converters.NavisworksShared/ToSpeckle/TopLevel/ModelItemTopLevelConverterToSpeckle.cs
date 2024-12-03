using Speckle.Converter.Navisworks.Settings;
using Speckle.Converter.Navisworks.ToSpeckle.PropertyHandlers;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Converter.Navisworks.ToSpeckle;

/// <summary>
/// Converts Navisworks ModelItem objects to Speckle Base objects.
/// </summary>
[NameAndRankValue(nameof(NAV.ModelItem), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class ModelItemToToSpeckleConverter : IToSpeckleTopLevelConverter
{
  private readonly ClassPropertiesExtractor _classPropertiesExtractor;
  private readonly PropertySetsExtractor _propertySetsExtractor;
  private readonly ModelPropertiesExtractor _modelPropertiesExtractor;
  private readonly StandardPropertyHandler _standardHandler;
  private readonly HierarchicalPropertyHandler _hierarchicalHandler;
  private readonly IConverterSettingsStore<NavisworksConversionSettings> _settingsStore;

  public ModelItemToToSpeckleConverter(
    IConverterSettingsStore<NavisworksConversionSettings> settingsStore,
    ClassPropertiesExtractor classPropertiesExtractor,
    PropertySetsExtractor propertySetsExtractor,
    StandardPropertyHandler standardHandler,
    HierarchicalPropertyHandler hierarchicalHandler,
    ModelPropertiesExtractor modelPropertiesExtractor
  )
  {
    _settingsStore = settingsStore;
    _classPropertiesExtractor = classPropertiesExtractor;
    _propertySetsExtractor = propertySetsExtractor;
    _standardHandler = standardHandler;
    _hierarchicalHandler = hierarchicalHandler;
    _modelPropertiesExtractor = modelPropertiesExtractor;
  }

  /// <summary>
  /// Converts a Navisworks object to a Speckle Base object.
  /// </summary>
  /// <param name="target">The object to convert.</param>
  /// <returns>The converted Speckle Base object.</returns>
  public Base Convert(object target)
  {
    if (target == null)
    {
      throw new ArgumentNullException(nameof(target));
    }

    return Convert((NAV.ModelItem)target);
  }

  // Converts a Navisworks ModelItem into a Speckle Base object
  private Base Convert(NAV.ModelItem target)
  {
    var name = GetObjectName(target);

    Base navisworksObject = target.HasGeometry ? CreateGeometryObject(target, name) : CreateNonGeometryObject(name);

    IPropertyHandler handler = ShouldMergeProperties(target) ? _hierarchicalHandler : _standardHandler;
    if (!_settingsStore.Current.User.ExcludeProperties)
    {
      handler.AssignProperties(navisworksObject, target);
    }

    return navisworksObject;
  }

  /// <summary>
  /// Determines whether properties should be merged from ancestors.
  /// Only geometry objects should have their properties merged.
  /// </summary>
  private static bool ShouldMergeProperties(NAV.ModelItem target) => target.HasGeometry;

  private static Base CreateGeometryObject(NAV.ModelItem target, string name) =>
    new()
    {
      ["name"] = name,
      ["displayValue"] = DisplayValueExtractor.GetDisplayValue(target),
      ["properties"] = new Dictionary<string, object?>()
    };

  private static Collection CreateNonGeometryObject(string name) =>
    new()
    {
      ["name"] = name,
      ["elements"] = new List<Base>(),
      ["properties"] = new Dictionary<string, object?>()
    };

  private static string GetObjectName(NAV.ModelItem target) =>
    target.ClassDisplayName ?? target.FindFirstObjectAncestor()?.ClassDisplayName ?? "Unnamed model";
}
