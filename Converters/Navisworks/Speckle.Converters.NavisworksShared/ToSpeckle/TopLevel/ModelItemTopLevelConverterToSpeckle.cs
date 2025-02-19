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
  public Base Convert(object target)
  {
    if (target == null)
    {
      throw new ArgumentNullException(nameof(target));
    }

    return Convert((NAV.ModelItem)target);
  }

  // Converts a Navisworks ModelItem into a Speckle Base object
  private Base Convert(NAV.ModelItem target) =>
    target.HasGeometry ? CreateGeometryObject(target) : CreateNonGeometryObject(target);

  private NavisworksObject CreateGeometryObject(NAV.ModelItem target)
  {
    IPropertyHandler handler = ShouldMergeProperties(target) ? _hierarchicalHandler : _standardHandler;
    var name = GetObjectName(target);
    return new NavisworksObject()
    {
      displayValue = _displayValueExtractor.GetDisplayValue(target),
      name = name,
      properties = _settingsStore.Current.User.ExcludeProperties ? [] : handler.GetProperties(target),
      units = _settingsStore.Current.Derived.SpeckleUnits,
    };
  }

  private static Collection CreateNonGeometryObject(NAV.ModelItem target)
  {
    var name = GetObjectName(target);
    return new Collection
    {
      name = name,
      elements = [],
      ["properties"] = new Dictionary<string, object?>()
    };
  }

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
