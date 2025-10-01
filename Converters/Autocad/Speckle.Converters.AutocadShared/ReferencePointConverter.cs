using Speckle.Converters.Autocad.Helpers;
using Speckle.Converters.Common;
using Speckle.DoubleNumerics;

namespace Speckle.Converters.Autocad;

/// <summary>
/// POC: reference point functionality needs to be revisited (we are currently baking in these transforms into all geometry using the point and vector converters, and losing the transform).
/// This converter uses the transform from the converter settings (from the current doc)
/// </summary>
public class ReferencePointConverter(IConverterSettingsStore<AutocadConversionSettings> converterSettings)
  : IReferencePointConverter
{
  public List<double> ConvertDoublesToExternalCoordinates(List<double> d)
  {
    if (d.Count % 3 != 0)
    {
      throw new ArgumentException("Point list of xyz values is malformed", nameof(d));
    }

    if (converterSettings.Current.ReferencePointTransform is AG.Matrix3d m)
    {
      Matrix4x4 transform = TransformHelper.ConvertToMatrix4x4(m.Inverse());

      var transformed = new List<double>(d.Count);

      for (int i = 0; i < d.Count; i += 3)
      {
        Vector3 p = Vector3.Transform(new(d[i], d[i + 1], d[i + 2]), transform);

        transformed.Add(p.X);
        transformed.Add(p.Y);
        transformed.Add(p.Z);
      }

      return transformed;
    }

    return d;
  }

  public AG.Point3d ConvertPointToExternalCoordinates(AG.Point3d p)
  {
    if (converterSettings.Current.ReferencePointTransform is AG.Matrix3d transform)
    {
      return p.TransformBy(transform.Inverse());
    }

    return p;
  }

  public AG.Vector3d ConvertVectorToExternalCoordinates(AG.Vector3d v)
  {
    if (converterSettings.Current.ReferencePointTransform is AG.Matrix3d transform)
    {
      return v.TransformBy(transform.Inverse());
    }

    return v;
  }
}
