namespace Speckle.Converters.Autocad;

public interface IReferencePointConverter
{
  AG.Point3d ConvertPointToExternalCoordinates(AG.Point3d p);

  AG.Point3d ConvertPointToInternalCoordinates(AG.Point3d p);

  AG.Vector3d ConvertVectorToExternalCoordinates(AG.Vector3d v);

  AG.Vector3d ConvertVectorToInternalCoordinates(AG.Vector3d v);
}
