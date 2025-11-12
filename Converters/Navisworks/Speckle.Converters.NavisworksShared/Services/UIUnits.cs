using Speckle.Converter.Navisworks.Helpers;
using Speckle.InterfaceGenerator;

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
