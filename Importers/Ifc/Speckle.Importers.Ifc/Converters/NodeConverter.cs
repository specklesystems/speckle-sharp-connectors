using System.Reflection;
using Speckle.Importers.Ifc.Ara3D.IfcParser;
using Speckle.Importers.Ifc.Types;
using Speckle.InterfaceGenerator;
using Speckle.Sdk.Models;

namespace Speckle.Importers.Ifc.Converters;

[GenerateAutoInterface]
public class NodeConverter(IGeometryConverter geometryConverter) : INodeConverter
{
  public Base Convert(IfcModel model, IfcNode node)
  {
    var b = new Base();
    if (node is IfcPropSet ps)
    {
      b["Name"] = ps.Name;
      b["GlobalId"] = ps.Guid;
    }

    // https://github.com/specklesystems/speckle-server/issues/1180
    b["ifc_type"] = node.Type;

    // This is required because "speckle_type" has no setter, but is backed by a private field.
    var baseType = typeof(Base);
    var typeField = baseType.GetField("_type", BindingFlags.Instance | BindingFlags.NonPublic);
    typeField?.SetValue(b, node.Type);

    // Guid is null for property values, and other Ifc entities not derived from IfcRoot
    b.applicationId = node.Guid;

    // This is the express ID used to identify an entity wihtin a file.
    b["expressID"] = node.Id;

    // Even if there is no geometry, this will return an empty collection.
    var geo = model.GetGeometry(node.Id);
    if (geo != null)
    {
      var c = geometryConverter.Convert(geo);
      if (c.elements.Count > 0)
        b["@displayValue"] = c.elements;
    }

    // Create the children
    var children = node.GetChildren().Select(x => Convert(model, x)).ToList();
    b["@elements"] = children;

    // Add the properties
    foreach (var p in node.GetPropSets())
    {
      // Only when there are actually some properties.
      if (p.NumProperties > 0)
      {
        var name = p.Name;
        if (string.IsNullOrWhiteSpace(name))
          name = $"#{p.Id}";
        b[name] = ToSpeckleDictionary(p);
      }
    }

    // TODO: add the "type" properties

    return b;
  }

  public static Dictionary<string, object?> ToSpeckleDictionary(IfcPropSet ps)
  {
    var d = new Dictionary<string, object?>();
    foreach (var p in ps.GetProperties())
      d[p.Name] = p.Value.ToJsonObject();
    return d;
  }
}
