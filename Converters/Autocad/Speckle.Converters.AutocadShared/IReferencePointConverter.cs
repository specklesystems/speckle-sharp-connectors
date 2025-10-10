namespace Speckle.Converters.Autocad;

public interface IReferencePointConverter
{
  List<double> ConvertDoublesToExternalCoordinates(List<double> d);

  AG.Point3d ConvertPointToExternalCoordinates(AG.Point3d p);

  AG.Vector3d ConvertVectorToExternalCoordinates(AG.Vector3d v);
}
