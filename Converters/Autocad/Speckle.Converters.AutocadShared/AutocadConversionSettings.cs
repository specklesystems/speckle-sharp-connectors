using Autodesk.AutoCAD.Geometry;

namespace Speckle.Converters.Autocad;

public record AutocadConversionSettings(Document Document, Matrix3d ReferencePointTransform, string SpeckleUnits);
