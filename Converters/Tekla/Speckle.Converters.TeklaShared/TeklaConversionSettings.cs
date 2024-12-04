using Tekla.Structures.Model;

namespace Speckle.Converters.TeklaShared;

public record TeklaConversionSettings(Model Document, bool SendRebarsAsSolid, string SpeckleUnits);
