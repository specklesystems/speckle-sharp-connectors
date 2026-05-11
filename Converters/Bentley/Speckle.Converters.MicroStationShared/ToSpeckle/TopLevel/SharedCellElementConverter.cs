using Speckle.Converter.MicroStation.Settings;
using Speckle.Converters.Common;
using Speckle.Sdk.Models;
using MgdSharedCell = Bentley.DgnPlatformNET.Elements.SharedCellElement;

namespace Speckle.Converter.MicroStation.ToSpeckle.TopLevel;

/// <summary>
/// Converts a managed <see cref="MgdSharedCell"/> (instance of a shared cell definition — like
/// AutoCAD block references) into a lightweight Speckle <see cref="Base"/> with the cell
/// definition name and instance metadata. Full transform extraction (rotation, scale, origin)
/// is a follow-up; for now we expose the discoverable properties so the element doesn't get
/// dropped silently.
/// </summary>
public class SharedCellElementConverter(IConverterSettingsStore<MicroStationConversionSettings> settingsStore)
{
  public Base Convert(MgdSharedCell mgdShared)
  {
    var s = settingsStore.Current;
    var applicationId = ((ulong)mgdShared.ElementId).ToString();

    return new Base
    {
      applicationId = applicationId,
      ["type"] = "SharedCellInstance",
      ["definitionName"] = mgdShared.CellName,
      ["definitionDescription"] = mgdShared.CellDescription,
      ["scale"] = new[] { mgdShared.Scale.X, mgdShared.Scale.Y, mgdShared.Scale.Z },
      ["isAnnotation"] = mgdShared.IsAnnotation,
      ["units"] = s.SpeckleUnits,
    };
  }
}
