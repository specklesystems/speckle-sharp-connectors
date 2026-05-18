using Speckle.Converter.Navisworks.Services;
using Speckle.Converter.Navisworks.Settings;
using Speckle.Converter.Navisworks.ToSpeckle.Raw;
using Speckle.Converters.Common;
using Speckle.Sdk.Models;

namespace Speckle.Converter.Navisworks.ToSpeckle;

public class DisplayValueExtractor(
  GeometryToSpeckleConverter geometryConverter,
  GeometryConversionContext geometryConversionContext,
  IElementSelectionService elementSelectionService,
  BoundingBoxToSpeckleRawConverter boundingBoxToSpeckleRawConverter,
  IConverterSettingsStore<NavisworksConversionSettings> settingsStore
)
{
  internal List<Base> GetDisplayValue(NAV.ModelItem modelItem)
  {
    if (modelItem == null)
    {
      throw new ArgumentNullException(nameof(modelItem));
    }

    if (!modelItem.HasGeometry || !elementSelectionService.IsVisible(modelItem))
    {
      return [];
    }

    if (geometryConversionContext.TryGetDisplayValue(modelItem, out var batchedDisplayValue))
    {
      return batchedDisplayValue;
    }

    if (settingsStore.Current.User.GeometryDetailLevel != GeometryDetailLevel.Lite)
    {
      return GeometryConverter.Convert(modelItem);
    }

    var boundingBox = TryGetBoundingBox(modelItem);
    if (boundingBox == null)
    {
      return [];
    }

    var box = boundingBoxToSpeckleRawConverter.Convert(boundingBox);
    return box == null ? [] : [box];
  }

  /// <summary>
  /// Gets the underlying geometry converter for accessing cache statistics.
  /// </summary>
  private GeometryToSpeckleConverter GeometryConverter { get; } =
    geometryConverter ?? throw new ArgumentNullException(nameof(geometryConverter));

  private static NAV.BoundingBox3D? TryGetBoundingBox(NAV.ModelItem modelItem)
  {
    var modelItemType = modelItem.GetType();

    // Different Navisworks versions expose this in different ways; probe known variants.
    var boundingBoxProperty = modelItemType.GetProperty("BoundingBox");
    if (boundingBoxProperty?.PropertyType == typeof(NAV.BoundingBox3D))
    {
      return boundingBoxProperty.GetValue(modelItem) as NAV.BoundingBox3D;
    }

    var boundingBoxMethod = modelItemType.GetMethod("BoundingBox", Type.EmptyTypes);
    if (boundingBoxMethod?.ReturnType == typeof(NAV.BoundingBox3D))
    {
      return boundingBoxMethod.Invoke(modelItem, null) as NAV.BoundingBox3D;
    }

    var getBoundingBoxMethod = modelItemType.GetMethod("GetBoundingBox", new[] { typeof(bool) });
    return getBoundingBoxMethod?.ReturnType == typeof(NAV.BoundingBox3D)
      ? getBoundingBoxMethod.Invoke(modelItem, [false]) as NAV.BoundingBox3D
      : null;
  }
}
