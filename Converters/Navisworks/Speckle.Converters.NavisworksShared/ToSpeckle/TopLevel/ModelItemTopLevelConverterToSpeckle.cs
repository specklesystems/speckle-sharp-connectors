using Speckle.Converter.Navisworks.Settings;
using Speckle.Converter.Navisworks.ToSpeckle.PropertyHandlers;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Data;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Converter.Navisworks.ToSpeckle;

/// <summary>
/// Converts Navisworks ModelItem objects to Speckle Base objects.
/// </summary>
[NameAndRankValue(typeof(NAV.ModelItem), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class ModelItemToToSpeckleConverter : IToSpeckleTopLevelConverter
{
  private readonly StandardPropertyHandler _standardHandler;
  private readonly HierarchicalPropertyHandler _hierarchicalHandler;
  private readonly IConverterSettingsStore<NavisworksConversionSettings> _settingsStore;
  private readonly DisplayValueExtractor _displayValueExtractor;

  public ModelItemToToSpeckleConverter(
    IConverterSettingsStore<NavisworksConversionSettings> settingsStore,
    StandardPropertyHandler standardHandler,
    HierarchicalPropertyHandler hierarchicalHandler,
    DisplayValueExtractor displayValueExtractor
  )
  {
    _settingsStore = settingsStore;
    _standardHandler = standardHandler;
    _hierarchicalHandler = hierarchicalHandler;
    _displayValueExtractor = displayValueExtractor;
  }

  /// <summary>
  /// Converts a Navisworks object to a Speckle Base object.
  /// </summary>
  /// <param name="target">The object to convert.</param>
  /// <returns>The converted Speckle Base object.</returns>
  public Base Convert(object target) =>
    target == null ? throw new ArgumentNullException(nameof(target)) : Convert((NAV.ModelItem)target);


  // Converts a Navisworks ModelItem into a Speckle Base object
  private Base Convert(NAV.ModelItem target)
  {
    IPropertyHandler handler = ShouldMergeProperties(target) ? _hierarchicalHandler : _standardHandler;
    var name = GetObjectName(target);

    return target.HasGeometry
      ? CreateGeometryObject(target, name, handler)
      : CreateNonGeometryObject(target, name, handler);
  }


  // There are in fact only two types of objects: geometry and non-geometry, the latter being collections of other objects
  private NavisworksObject CreateGeometryObject(NAV.ModelItem target, string name, IPropertyHandler propertyHandler) =>
    new()
    {
      name = name,
      displayValue = _displayValueExtractor.GetDisplayValue(target),
      properties = _settingsStore.Current.User.ExcludeProperties ? [] : propertyHandler.GetProperties(target),
      units = _settingsStore.Current.Derived.SpeckleUnits,
    };


  private Collection CreateNonGeometryObject(NAV.ModelItem target, string name, IPropertyHandler propertyHandler) =>
    new()
    {
      name = name,
      elements = [],
      ["properties"] = _settingsStore.Current.User.ExcludeProperties ? [] : propertyHandler.GetProperties(target),
    };

  /// <summary>
  /// Determines whether properties should be merged from ancestors.
  /// Only geometry objects should have their properties merged.... For now.
  /// </summary>
  private static bool ShouldMergeProperties(NAV.ModelItem target) => target.HasGeometry;

  private static string GetObjectName(NAV.ModelItem target)
  {
    var targetName = target.DisplayName;

    var firstObjectAncestor = target.FindFirstObjectAncestor();

    // while the target node name is null keep cycling through parent objects until displayname is not null or empty OR object is firstObjectAncestor

    while (string.IsNullOrEmpty(targetName) && target != firstObjectAncestor)
    {
      target = target.Parent;
      targetName = target.DisplayName;
    }

    if (string.IsNullOrEmpty(targetName))
    {
      targetName = "Unnamed model item";
    }

    return targetName;
  }
}
