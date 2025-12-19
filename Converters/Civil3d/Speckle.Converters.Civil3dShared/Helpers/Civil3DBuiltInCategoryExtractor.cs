using Speckle.InterfaceGenerator;

namespace Speckle.Converters.Civil3dShared.Helpers;

[GenerateAutoInterface]
public class Civil3DBuiltInCategoryExtractor : ICivil3DBuiltInCategoryExtractor
{
  internal const string DEFAULT_DICT_KEY = "builtInCategory";

  public bool TryGetBuiltInCategory(ADB.Entity entity, out string mapped)
  {
    mapped = string.Empty;

    var rx = entity.GetRXClass();
    var name = rx?.Name;

    if (string.IsNullOrWhiteSpace(name))
    {
      return false;
    }

    var builtInCategory = Civil3DClassToRevitBuiltInCategory(name!);

    if (string.Equals(builtInCategory, name, StringComparison.OrdinalIgnoreCase))
    {
      return false; // no mapping
    }

    mapped = builtInCategory;

    return true;
  }

  private static readonly Dictionary<string, string> s_civil3dClassMap =
    new(StringComparer.OrdinalIgnoreCase)
    {
      ["Pipe"] = "OST_PlaceHolderPipes",
      // ["Structure"] = "OST_PipeFitting"
    };

  private static string Civil3DClassToRevitBuiltInCategory(string className) =>
    s_civil3dClassMap.TryGetValue(className, out var ost) ? ost : className;
}
