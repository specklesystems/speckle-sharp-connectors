using Rhino;

namespace Speckle.Converters.Rhino;

public record RhinoConversionSettings(RhinoDoc Document, string SpeckleUnits, bool ModelFarFromOrigin);
