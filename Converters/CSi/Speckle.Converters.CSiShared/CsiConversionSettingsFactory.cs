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
    List<string>? selectedResultTypes = null,
    bool sendVolumetricGeometry = false
  )
  {
    // NOTE: only applicable to ETABS. If we bring in SAP2000 then we need to revert to GetPresentUnits
    // NOTE: change from GetPresentUnits as this was linked to weird behaviour (see CNX-2621), returning "0" sometimes
    // bug in the GetPresentUnits api call ...
    eTemperature temperatureUnit = eTemperature.NotApplicable;
    eLength lengthUnit = eLength.NotApplicable;
    eForce forceUnit = eForce.NotApplicable;
    if (document.GetPresentUnits_2(ref forceUnit, ref lengthUnit, ref temperatureUnit) != 0)
    {
      throw new InvalidOperationException("Failed to read ETABS API units before conversion.");
    }

    string speckleUnits = unitsConverter.ConvertOrThrow(lengthUnit);

    // ENG-10421: ETABS can retain database-unit geometry after opening a file in different present units.
    // Reapplying the same units refreshes its API conversion factors without changing the selected units.
    if (document.SetPresentUnits_2(forceUnit, lengthUnit, temperatureUnit) != 0)
    {
      throw new InvalidOperationException("Failed to synchronize ETABS API units before conversion.");
    }

    return new CsiConversionSettings(
      document,
      speckleUnits,
      selectedLoadCasesAndCombinations ?? [],
      selectedResultTypes ?? [],
      sendVolumetricGeometry
    );
  }
}
