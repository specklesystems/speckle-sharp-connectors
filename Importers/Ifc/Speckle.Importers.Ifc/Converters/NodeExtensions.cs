using Speckle.Importers.Ifc.Ara3D.IfcParser;
using Speckle.Importers.Ifc.Ara3D.IfcParser.Schema;

namespace Speckle.Importers.Ifc.Converters;

public static class NodeExtensions
{
  public static Dictionary<string, object?> ConvertPropertySets(this IfcNode node)
  {
    var result = new Dictionary<string, object?>();
    foreach (var p in node.GetPropSets())
    {
      if (p.NumProperties <= 0)
        continue;

      var name = p.Name;
      if (string.IsNullOrWhiteSpace(name))
        name = $"#{p.Id}";
      result[name] = ToSpeckleDictionary(p);
    }

    return result;
  }

  public static Dictionary<string, object?> ToSpeckleDictionary(this IfcPropSet ps)
  {
    var d = new Dictionary<string, object?>();
    foreach (var p in ps.GetProperties())
      d[p.Name] = p.Value.ToJsonObject();
    return d;
  }
}
