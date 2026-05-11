using Speckle.Converter.MicroStation.Settings;
using Speckle.Converters.Common;
using Speckle.Sdk.Models;
using MgdSurfaceOrSolid = Bentley.DgnPlatformNET.Elements.SurfaceOrSolidElement;

namespace Speckle.Converter.MicroStation.ToSpeckle.TopLevel;

/// <summary>
/// Converts a managed <see cref="MgdSurfaceOrSolid"/> (3D solid, swept surface, etc.) into a
/// bounding-box Speckle <see cref="Base"/>. Real B-rep tessellation through
/// <c>SolidPrimitiveQuery</c> + <c>PolyfaceConstruction</c> is the follow-up — that path was
/// historically CSE-prone in this codebase, so we deliberately ship a placeholder until the
/// tessellation pipeline is hardened.
/// </summary>
public class SolidElementConverter(
  IConverterSettingsStore<MicroStationConversionSettings> settingsStore,
  FallbackElementMeshConverter fallbackConverter
)
{
  public Base Convert(MgdSurfaceOrSolid mgdSolid)
  {
    // Defer to the bounding-box fallback. settingsStore parameter retained for symmetry with
    // the other converters and so the (eventually-real) tessellation path has access to units.
    _ = settingsStore;
    return fallbackConverter.Convert(mgdSolid);
  }
}
