namespace Speckle.Converters.Autocad;

public record AutocadConversionSettings(Document Document, AG.Matrix3d? ReferencePointTransform, string SpeckleUnits);
