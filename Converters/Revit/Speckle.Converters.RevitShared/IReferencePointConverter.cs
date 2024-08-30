namespace Speckle.Converters.RevitShared;

public interface IReferencePointConverter
{
  DB.XYZ ConvertToExternalCoordinates(DB.XYZ p, bool isPoint);

  DB.XYZ ConvertToInternalCoordinates(DB.XYZ p, bool isPoint);
}
