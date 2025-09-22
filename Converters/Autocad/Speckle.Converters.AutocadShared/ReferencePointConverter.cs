using Autodesk.AutoCAD.Geometry;
using Speckle.Converters.Common;

namespace Speckle.Converters.Autocad;

/// <summary>
/// POC: reference point functionality needs to be revisited (we are currently baking in these transforms into all geometry using the point and vector converters, and losing the transform).
/// This converter uses the transform from the converter settings (from the current doc)
/// </summary>
public class ReferencePointConverter(IConverterSettingsStore<AutocadConversionSettings> converterSettings)
  : IReferencePointConverter
{
  public AG.Point3d ConvertPointToExternalCoordinates(AG.Point3d p)
  {
    if (converterSettings.Current.ReferencePointTransform is Matrix3d transform)
    {
      return p.TransformBy(transform.Inverse());
    }

    return p;
  }

  public AG.Point3d ConvertPointToInternalCoordinates(AG.Point3d p)
  {
    if (converterSettings.Current.ReferencePointTransform is Matrix3d transform)
    {
      return p.TransformBy(transform);
    }

    return p;
  }

  public AG.Vector3d ConvertVectorToExternalCoordinates(AG.Vector3d v)
  {
    if (converterSettings.Current.ReferencePointTransform is Matrix3d transform)
    {
      return v.TransformBy(transform.Inverse());
    }

    return v;
  }

  public AG.Vector3d ConvertVectorToInternalCoordinates(AG.Vector3d v)
  {
    if (converterSettings.Current.ReferencePointTransform is Matrix3d transform)
    {
      return v.TransformBy(transform);
    }

    return v;
  }
}
