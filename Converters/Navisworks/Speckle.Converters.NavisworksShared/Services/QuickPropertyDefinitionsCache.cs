using Speckle.InterfaceGenerator;

namespace Speckle.Converter.Navisworks.Services;

[GenerateAutoInterface]
public class QuickPropertyDefinitionsCache : IQuickPropertyDefinitionsCache
{
  private IReadOnlyList<QuickPropertyDefinition>? _definitions;

  public IReadOnlyList<QuickPropertyDefinition> Ensure()
  {
    if (_definitions != null)
    {
      return _definitions;
    }

    _definitions = QuickPropertyDefinitionsUtil.Load();
    return _definitions;
  }

  public void Reset() => _definitions = null;
}
