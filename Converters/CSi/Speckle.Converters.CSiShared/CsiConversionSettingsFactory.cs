using Speckle.Converters.Common;
using Speckle.InterfaceGenerator;

namespace Speckle.Converters.CSiShared;

[GenerateAutoInterface]
public class CsiConversionSettingsFactory(
  IHostToSpeckleUnitConverter<eLength> unitsConverter,
  IConverterSettingsStore<CsiConversionSettings> settingsStore
) : ICsiConversionSettingsFactory
{
  public CsiConversionSettings Current => settingsStore.Current;

  public CsiConversionSettings Create(
    cSapModel document,
    List<string>? selectedLoadCasesAndCombinations = null,
    List<string>? selectedResultTypes = null
  )
  {
    // NOTE: only applicable to ETABS. If we bring in SAP2000 then we need to revert to GetPresentUnits
    // NOTE: change from GetPresentUnits as this was linked to weird behaviour (see CNX-2621), returning "0" sometimes
    // bug in the GetPresentUnits api call ...
    eTemperature temperatureUnit = eTemperature.NotApplicable;
    eLength lengthUnit = eLength.NotApplicable;
    eForce forceUnit = eForce.NotApplicable;
    document.GetPresentUnits_2(ref forceUnit, ref lengthUnit, ref temperatureUnit);

    return new CsiConversionSettings(
      document,
      unitsConverter.ConvertOrThrow(lengthUnit),
      selectedLoadCasesAndCombinations ?? [],
      selectedResultTypes ?? []
    );
  }
}
