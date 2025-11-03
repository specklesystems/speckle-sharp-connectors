using Speckle.Converters.Autocad;
using Speckle.Converters.Autocad.Extensions;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common;

namespace Speckle.Converters.Autocad2023.ToHost.Raw;

public class AutocadPolycurveToHostPolylineRawConverter : ITypedConverter<SOG.Autocad.AutocadPolycurve, ADB.Polyline>
{
  private readonly ITypedConverter<SOG.Vector, AG.Vector3d> _vectorConverter;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public AutocadPolycurveToHostPolylineRawConverter(
    ITypedConverter<SOG.Vector, AG.Vector3d> vectorConverter,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  )
  {
    _vectorConverter = vectorConverter;
    _settingsStore = settingsStore;
  }

  public ADB.Polyline Convert(SOG.Autocad.AutocadPolycurve target)
  {
    if (target.normal is null || target.elevation is null)
    {
      throw new ArgumentException("Autocad polycurve of type light did not have a valid normal and/or elevation");
    }

    // convert the normal, get vertices and transform them to ocs (extension method supports both 2d and 3d polyline vertices)
    AG.Vector3d normal = _vectorConverter.Convert(target.normal);
    double f = Units.GetConversionFactor(target.units, _settingsStore.Current.SpeckleUnits);
    double elevation = (double)target.elevation;
    List<AG.Point3d> points3d = target.value.ConvertPolylineValueToPoint3dInOcs(normal, elevation, f);

    ADB.Polyline polyline =
      new()
      {
        Normal = normal,
        Elevation = elevation * f,
        Closed = target.closed
      };

    for (int i = 0; i < points3d.Count; i++)
    {
      var bulge = target.bulges is null ? 0 : target.bulges[i];
      polyline.AddVertexAt(i, new(points3d[i].X, points3d[i].Y), bulge, 0, 0);
    }

    return polyline;
  }
}
