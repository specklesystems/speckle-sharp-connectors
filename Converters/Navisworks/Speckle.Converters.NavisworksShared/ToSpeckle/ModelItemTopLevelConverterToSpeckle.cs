﻿using Speckle.Converter.Navisworks.Models;
using Speckle.Converter.Navisworks.Settings;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converter.Navisworks.ToSpeckle;

/// <summary>
/// Converts Navisworks ModelItem objects to Speckle Base objects.
/// </summary>
public class ModelItemTopLevelConverterToSpeckle : IToSpeckleTopLevelConverter
{
  private readonly DisplayValueExtractor _displayValueExtractor;
  private readonly ClassPropertiesExtractor _classPropertiesExtractor;
  private readonly PropertySetsExtractor _propertySetsExtractor;
  private readonly IConverterSettingsStore<NavisworksConversionSettings> _settingsStore;

  /// <summary>
  /// Initializes a new instance of the <see cref="ModelItemTopLevelConverterToSpeckle"/> class.
  /// </summary>
  /// <param name="displayValueExtractor">Extractor for geometric display values.</param>
  /// <param name="classPropertiesExtractor">Extractor for class-specific properties.</param>
  /// <param name="propertySetsExtractor">Extractor for property sets.</param>
  /// <param name="settingsStore">Converter settings store.</param>

  public ModelItemTopLevelConverterToSpeckle(
    DisplayValueExtractor displayValueExtractor,
    ClassPropertiesExtractor classPropertiesExtractor,
    PropertySetsExtractor propertySetsExtractor,
    IConverterSettingsStore<NavisworksConversionSettings> settingsStore
  )
  {
    _displayValueExtractor = displayValueExtractor;
    _classPropertiesExtractor = classPropertiesExtractor;
    _propertySetsExtractor = propertySetsExtractor;
    _settingsStore = settingsStore;
  }

  /// <summary>
  /// Converts a Navisworks object to a Speckle Base object.
  /// </summary>
  /// <param name="target">The object to convert.</param>
  /// <returns>The converted Speckle Base object.</returns>
  public Base Convert(object target) => Convert((NAV.ModelItem)target);

  // Converts a Navisworks ModelItem into a Speckle Base object
  private Base Convert(NAV.ModelItem target)
  {
    var name = GetObjectName(target);

    INavisworksObject navisworksObject = target.HasGeometry
      ? CreateGeometryObject(target, name) // Create a NavisworksGeometryObject
      : CreateNonGeometryObject(name); // Create a NavisworksObject

    AddProperties(navisworksObject, target);

    return (Base)navisworksObject;
  }

  private NavisworksGeometryObject CreateGeometryObject(NAV.ModelItem target, string name) =>
    new(name: name, displayValue: _displayValueExtractor.GetDisplayValue(target).ToList());

  private NavisworksObject CreateNonGeometryObject(string name) =>
    new(name) { elements = new List<NavisworksObject>() };

  private string GetObjectName(NAV.ModelItem target) =>
    target.ClassDisplayName ?? target.FindFirstObjectAncestor()?.ClassDisplayName ?? "Unnamed model";

  // Adds class and property set data to the object
  private void AddProperties(INavisworksObject navisworksObject, NAV.ModelItem target)
  {
    if (_settingsStore.Current.ExcludeProperties)
    {
      return;
    }

    // Add class properties
    var classProperties = _classPropertiesExtractor.GetClassProperties(target);
    if (classProperties != null)
    {
      foreach (var kvp in classProperties)
      {
        navisworksObject.AddProperty(kvp.Key, kvp.Value);
      }
    }

    // Add property sets
    var propertySets = _propertySetsExtractor.GetPropertySets(target);

    if (propertySets != null)
    {
      navisworksObject.AddProperty("properties", propertySets);
    }
  }
}
