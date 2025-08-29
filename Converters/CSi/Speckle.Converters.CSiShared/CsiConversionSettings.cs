namespace Speckle.Converters.CSiShared;

public record CsiConversionSettings(
  cSapModel SapModel,
  string SpeckleUnits,
  List<string>? SelectedLoadCasesAndCombinations = null,
  List<string>? SelectedResultTypes = null
);
