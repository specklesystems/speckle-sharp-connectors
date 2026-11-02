using Speckle.Converters.Autocad;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Plant3dShared.Helpers;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Plant3dShared.ToSpeckle.Geometry;

/// <summary>
/// Converts a <see cref="ADB.Face"/> (a legacy AutoCAD 3D face with 3 or 4 corners)
/// to a <see cref="SOG.Mesh"/>.
/// </summary>
[NameAndRankValue(typeof(ADB.Face), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class FaceToSpeckleConverter(
  IReferencePointConverter referencePointConverter,
  IConverterSettingsStore<AutocadConversionSettings> settingsStore
) : IToSpeckleTopLevelConverter
{
  public Base Convert(object target) => Convert((ADB.Face)target);

  public SOG.Mesh Convert(ADB.Face target) =>
    QuadMeshBuilder.Build(
      target.GetVertexAt(0),
      target.GetVertexAt(1),
      target.GetVertexAt(2),
      target.GetVertexAt(3),
      referencePointConverter,
      settingsStore
    );
}
