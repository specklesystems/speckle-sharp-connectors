namespace Speckle.Connectors.CSiShared.Builders;

/// <summary>
/// Reads the model's DATABASE units (<c>GetDatabaseUnits_2</c>) — the unit system analysis results are
/// expressed in, distinct from the present/display units behind <c>SpeckleUnits</c>. Values are the raw CSi
/// enum names; a failed call (its return code is not surfaced) leaves them <c>"NotApplicable"</c>.
/// </summary>
internal static class CsiDatabaseUnits
{
  public static (string Force, string Temperature) GetForceAndTemperature(cSapModel sapModel)
  {
    var forceUnit = eForce.NotApplicable;
    var lengthUnit = eLength.NotApplicable;
    var temperatureUnit = eTemperature.NotApplicable;

    sapModel.GetDatabaseUnits_2(ref forceUnit, ref lengthUnit, ref temperatureUnit);

    return (forceUnit.ToString(), temperatureUnit.ToString());
  }
}
