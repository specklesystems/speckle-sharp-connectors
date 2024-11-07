using Tekla.Structures.Model;

namespace Speckle.Converter.Tekla2024;

public record TeklaConversionSettings(Model Document, bool SendRebarsAsSolid, string SpeckleUnits);
