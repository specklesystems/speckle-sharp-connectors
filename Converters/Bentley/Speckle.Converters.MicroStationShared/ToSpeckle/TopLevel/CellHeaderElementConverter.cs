using Speckle.Converter.MicroStation.Settings;
using Speckle.Converters.Common;
using Speckle.Sdk.Models.Collections;
using MgdCellHeader = Bentley.DgnPlatformNET.Elements.CellHeaderElement;

namespace Speckle.Converter.MicroStation.ToSpeckle.TopLevel;

/// <summary>
/// Converts a managed <see cref="MgdCellHeader"/> (named group, anonymous compound, or shared
/// cell instance) into a SKELETON Speckle <see cref="Collection"/> — name + element-type tag,
/// no children. The dispatcher (<c>MicroStationRootToSpeckleConverter</c>) is responsible for
/// walking <c>cell.GetChildren()</c> and recursively converting each child via its own
/// <c>Convert()</c> method.
/// <para>
/// Why the dispatcher does the recursion instead of this converter: a cell can contain any
/// element type, so the converter would have to call back into the root dispatcher — which
/// creates a DI circular dependency since the root dispatcher is constructed with this
/// converter as a leaf. Civil3D / Revit / Tekla avoid the issue by recursing on the same type
/// (Alignment → Profile, ModelObject → ModelObject children), but a DGN cell can mix any
/// types. Putting the recursion in the dispatcher keeps every leaf converter pure (no
/// back-references) and avoids <c>Lazy&lt;&gt;</c> / service-locator workarounds.
/// </para>
/// <para>
/// The managed API gives us all cell flavors via this single type — see <c>CellName</c>,
/// <c>IsAnonymous</c>, <c>IsSharedCell</c>, <c>IsPointCell</c> properties for classification.
/// </para>
/// </summary>
public class CellHeaderElementConverter(IConverterSettingsStore<MicroStationConversionSettings> settingsStore)
{
  public Collection Convert(MgdCellHeader mgdCell)
  {
    var s = settingsStore.Current;
    var applicationId = ((ulong)mgdCell.ElementId).ToString();
    var name = string.IsNullOrEmpty(mgdCell.CellName) ? applicationId : mgdCell.CellName;

    return new Collection
    {
      name = name,
      ["elementType"] = mgdCell.IsSharedCell ? "SharedCell" : (mgdCell.IsAnonymous ? "ComplexHeader" : "Cell"),
      ["units"] = s.SpeckleUnits,
      applicationId = applicationId,
      // elements list intentionally left empty — dispatcher fills it via recursive Convert calls.
    };
  }
}
