namespace Speckle.Converters.Civil3dShared;

public record Civil3dConversionSettings(Document Document, string SpeckleUnits, bool MappingToRevitCategories = false);
