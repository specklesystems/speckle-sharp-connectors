using Speckle.Converter.Navisworks.Settings;
using Speckle.Converter.Navisworks.ToSpeckle.PropertyHandlers;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Data;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Converter.Navisworks.ToSpeckle;

[NameAndRankValue(typeof(NAV.ModelItem), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class ModelItemToToSpeckleConverter(
  IConverterSettingsStore<NavisworksConversionSettings> settingsStore,
  StandardPropertyHandler standardHandler,
  HierarchicalPropertyHandler hierarchicalHandler,
  DisplayValueExtractor displayValueExtractor
) : IToSpeckleTopLevelConverter
{
  public Base Convert(object target) =>
    target == null ? throw new ArgumentNullException(nameof(target)) : Convert((NAV.ModelItem)target);

  private Base Convert(NAV.ModelItem target)
  {
    IPropertyHandler handler = ShouldMergeProperties(target) ? hierarchicalHandler : standardHandler;
    var name = GetObjectName(target);

    return target.HasGeometry
      ? CreateGeometryObject(target, name, handler)
      : CreateNonGeometryObject(target, name, handler);
  }

  private NavisworksObject CreateGeometryObject(NAV.ModelItem target, string name, IPropertyHandler propertyHandler)
  {
    var displayValue = displayValueExtractor.GetDisplayValue(target);

    var geometryObject = new NavisworksObject
    {
      units = settingsStore.Current.Derived.SpeckleUnits,
      name = name,
      properties = settingsStore.Current.User.ExcludeProperties ? [] : propertyHandler.GetProperties(target),
      displayValue = displayValue
    };

    return geometryObject;
  }

  private Collection CreateNonGeometryObject(NAV.ModelItem target, string name, IPropertyHandler propertyHandler) =>
    new()
    {
      name = name,
      elements = [],
      ["properties"] = settingsStore.Current.User.ExcludeProperties ? [] : propertyHandler.GetProperties(target),
    };

  private static bool ShouldMergeProperties(NAV.ModelItem target) => target.HasGeometry;

  private static string GetObjectName(NAV.ModelItem target)
  {
    var targetName = target.DisplayName;

    var firstObjectAncestor = target.FindFirstObjectAncestor();

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
