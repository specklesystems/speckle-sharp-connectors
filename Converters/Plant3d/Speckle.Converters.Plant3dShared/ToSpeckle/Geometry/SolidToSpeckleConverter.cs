using Speckle.Converters.Autocad;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Plant3dShared.Helpers;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Plant3dShared.ToSpeckle.Geometry;

/// <summary>
/// Converts a <see cref="ADB.Solid"/> (a legacy AutoCAD 2D filled solid with 3 or 4
/// coplanar corners, created by the SOLID command) to a <see cref="SOG.Mesh"/>.
/// </summary>
/// <remarks>
/// <see cref="ADB.Solid.Thickness"/> is ignored, consistent with the Line, Arc, Circle and Polyline converters.
/// </remarks>
[NameAndRankValue(typeof(ADB.Solid), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class SolidToSpeckleConverter(
  IReferencePointConverter referencePointConverter,
  IConverterSettingsStore<AutocadConversionSettings> settingsStore
) : IToSpeckleTopLevelConverter
{
  public Base Convert(object target) => Convert((ADB.Solid)target);

  public SOG.Mesh Convert(ADB.Solid target) =>
    // Reverse the last 2 vertices of the Solid to convert from Z-order to winding order.
    QuadMeshBuilder.Build(
      target.GetPointAt(0),
      target.GetPointAt(1),
      target.GetPointAt(3),
      target.GetPointAt(2),
      referencePointConverter,
      settingsStore
    );
}
