using Speckle.Converter.MicroStation.Settings;
using Speckle.Converters.Common;
using Speckle.DoubleNumerics;
using Speckle.Objects.Other;
using Speckle.Sdk.Models;

namespace Speckle.Converter.MicroStation.ToSpeckle.TopLevel;

/// <summary>
/// Converts a MicroStation 2026 COM <see cref="MSIDGN.SharedCellElement"/> (instance of a shared
/// cell / block definition) to a Speckle <see cref="Base"/> with an embedded <see cref="Transform"/>.
/// The COM API exposes <c>Origin</c>, <c>Scale</c>, and <c>Rotation</c> (Matrix3d) directly in master units.
/// </summary>
[NameAndRankValue(typeof(MSIDGN.SharedCellElement), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class SharedCellElementConverter(IConverterSettingsStore<MicroStationConversionSettings> settingsStore)
  : IToSpeckleTopLevelConverter
{
  public Base Convert(object target) => Convert((MSIDGN.SharedCellElement)target);

  private Base Convert(MSIDGN.SharedCellElement element)
  {
    var s = settingsStore.Current;

    var rot = element.Rotation;
    var scale = element.Scale;
    var origin = element.Origin;

    // Build a column-major 4×4 transform matrix combining rotation, scale, and translation.
    // Matrix3d.RowX/Y/Z are the local axes; scale is applied per axis; origin is the translation.
    // Matrix4x4 is column-dominant for Speckle instance transforms (Mij = row i, column j).
    var matrix = new Matrix4x4
    {
      M11 = rot.RowX.X * scale.X,
      M12 = rot.RowX.Y * scale.X,
      M13 = rot.RowX.Z * scale.X,
      M14 = origin.X,
      M21 = rot.RowY.X * scale.Y,
      M22 = rot.RowY.Y * scale.Y,
      M23 = rot.RowY.Z * scale.Y,
      M24 = origin.Y,
      M31 = rot.RowZ.X * scale.Z,
      M32 = rot.RowZ.Y * scale.Z,
      M33 = rot.RowZ.Z * scale.Z,
      M34 = origin.Z,
      M41 = 0,
      M42 = 0,
      M43 = 0,
      M44 = 1,
    };

    var transform = new Transform
    {
      matrix = matrix,
      units = s.SpeckleUnits,
    };

    return new Base
    {
      applicationId = element.ID.ToString(),
      ["type"] = "SharedCellInstance",
      ["definitionName"] = element.Name,
      ["transform"] = transform,
      ["units"] = s.SpeckleUnits,
    };
  }
}
