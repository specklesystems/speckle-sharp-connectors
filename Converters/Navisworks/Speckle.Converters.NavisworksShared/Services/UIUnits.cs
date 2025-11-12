using Autodesk.Navisworks.Api.Interop;
using Speckle.InterfaceGenerator;
using static Autodesk.Navisworks.Api.Interop.LcUOption;

namespace Speckle.Converter.Navisworks.Services;

[GenerateAutoInterface]
public class UiUnitsCache : IUiUnitsCache
{
  private NAV.Units? _ui;

  public NAV.Units Ensure()
  {
    if (_ui.HasValue)
    {
      return _ui.Value;
    }

    UiUnitsUtil.TryGetUiLinearUnits(out var ui);
    _ui = ui;
    return _ui.Value;
  }

  public void Reset() => _ui = null;
}

public static class UiUnitsUtil
{
  // disp_units: 0=linear_format
  public static bool TryGetUiLinearUnits(out NAV.Units uiUnits)
  {
    using var opt = new LcUOptionLock();
    var root = GetRoot(opt);
    var disp = root.GetSubOptions("interface").GetSubOptions("disp_units");

    int code = -1;

    using var v = new NAV.VariantData();
    disp.GetValue(0, v);
    var s = v.ToString();
    var colon = s.LastIndexOf(':');
    var open = s.IndexOf('(', colon + 1);
    if (colon >= 0 && open > colon && !int.TryParse(s.Substring(colon + 1, open - colon - 1), out code))
    {
      code = -1;
    }

    uiUnits = code switch
    {
      0 => NAV.Units.Kilometers,
      1 => NAV.Units.Meters,
      2 => NAV.Units.Centimeters,
      3 => NAV.Units.Millimeters,
      4 => NAV.Units.Micrometers,
      5 => NAV.Units.Miles,
      6 => NAV.Units.Miles,
      7 => NAV.Units.Yards,
      8 => NAV.Units.Yards,
      9 => NAV.Units.Feet,
      10 => NAV.Units.Feet,
      11 => NAV.Units.Feet,
      12 => NAV.Units.Inches,
      13 => NAV.Units.Inches,
      14 => NAV.Units.Mils,
      15 => NAV.Units.Microinches,
      _ => NAV.Units.Meters
    };

    return code >= 0;
  }
}
