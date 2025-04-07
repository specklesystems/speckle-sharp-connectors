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
      var name = p.Name;
      if (string.IsNullOrWhiteSpace(name))
      {
        name = $"#{p.Id}";
      }

      var dict = ToSpeckleDictionary(p);
      if (dict.Count > 0) //Ignore any empty psets, since they can bloat the data size
      {
        result[name] = dict;
      }
    }

    return result;
  }

  public static Dictionary<string, object?> ToSpeckleDictionary(this IfcPropSet ps)
  {
    var d = new Dictionary<string, object?>();
    foreach (var p in ps.GetProperties())
    {
      var value = p.Value.ToJsonObject();

      if (value is not null)
      {
        // Ignoring null values since they'd otherwise bloat the data size of speckle models.
        // Semantically, "null valued" and "not there" are different, but very few users care about the distinction.
        d[p.Name] = value;
      }
    }
    return d;
  }
}
