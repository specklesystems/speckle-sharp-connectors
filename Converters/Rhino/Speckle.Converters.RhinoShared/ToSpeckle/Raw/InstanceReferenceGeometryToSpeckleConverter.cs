using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.DoubleNumerics;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Converters.Rhino.ToSpeckle.Raw;

public class InstanceReferenceGeometryToSpeckleConverter : ITypedConverter<RG.InstanceReferenceGeometry, InstanceProxy>
{
  private readonly IConverterSettingsStore<RhinoConversionSettings> _settingsStore;

  public InstanceReferenceGeometryToSpeckleConverter(IConverterSettingsStore<RhinoConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  /// <summary>
  /// Converts a instance reference geometry object to a Speckle Instance proxy.
  /// </summary>
  /// <param name="target">The instance reference geometry object to convert.</param>
  /// <returns>The converted Speckle Instance proxy.</returns>
  /// <remarks>
  /// ⚠️ This conversion does not respect the instance definition and just creates a transform.
  /// </remarks>
  public InstanceProxy Convert(RG.InstanceReferenceGeometry target)
  {
    var t = target.Xform;
    var m = new Matrix4x4()
    {
      M11 = t.M00,
      M12 = t.M01,
      M13 = t.M02,
      M14 = t.M03,

      M21 = t.M10,
      M22 = t.M11,
      M23 = t.M12,
      M24 = t.M13,

      M31 = t.M20,
      M32 = t.M21,
      M33 = t.M22,
      M34 = t.M23,

      M41 = t.M30,
      M42 = t.M31,
      M43 = t.M32,
      M44 = t.M33
    };

    return new InstanceProxy()
    {
      definitionId = target.ParentIdefId.ToString(),
      maxDepth = 0, // default value since this is to omuch to calculate and will be done in connectors
      transform = m,
      units = _settingsStore.Current.SpeckleUnits
    };
  }
}
