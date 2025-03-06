using Speckle.Importers.Ifc.Ara3D.IfcParser;
using Speckle.Importers.Ifc.Types;
using Speckle.InterfaceGenerator;
using Speckle.Objects.Data;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Importers.Ifc.Converters;

[GenerateAutoInterface]
public class NodeConverter(IGeometryConverter geometryConverter) : INodeConverter
{
  /// <summary>
  /// Converts objects that inherit IfcRoot class
  /// </summary>
  /// <param name="node"></param>
  /// <returns></returns>
  private Collection ConvertCollection(IfcModel model, IfcNode node)
  {
    if (!node.IsIfcRoot)
      throw new ArgumentException("Expected to be an IfcRoot", paramName: nameof(node));

    return new Collection()
    {
      name = node.Name ?? node.Guid,
      applicationId = node.Guid,
      elements = ConvertChildren(model, node),
      ["ifc_type"] = node.Type,
      ["expressID"] = node.Id,
      ["properties"] = ConvertPropertySets(node),
    };
  }

  public Base Convert(IfcModel model, IfcNode node)
  {
    if (!node.IsIfcRoot)
      throw new ArgumentException("Expected to be an IfcRoot", paramName: nameof(node));

    if (node is IfcPropSet)
    {
      return new Base();
    }

    return node.Type switch
    {
      "IFCPROJECT" or "IFCSITE" or "IFCBUILDING" or "IFCBUILDINGSTOREY" => ConvertCollection(model, node),
      _ => ConvertDataObject(model, node)
    };
  }

  private List<Base> ConvertChildren(IfcModel model, IfcNode node)
  {
    return node.GetChildren().Where(x => x.IsIfcRoot).Select(x => Convert(model, x)).ToList();
  }

  public DataObject ConvertDataObject(IfcModel model, IfcNode node)
  {
    if (!node.IsIfcRoot)
      throw new ArgumentException("Expected to be an IfcRoot", paramName: nameof(node));

    // Even if there is no geometry, this will return an empty collection.
    var geo = model.GetGeometry(node.Id);
    List<Base> displayValue = geo != null ? geometryConverter.Convert(geo) : new();

    // TODO: add the "type" properties

    return new DataObject()
    {
      applicationId = node.Guid, // Guid is null for property values, and other Ifc entities not derived from IfcRoot
      properties = ConvertPropertySets(node),
      name = node.Name ?? node.Guid,
      displayValue = displayValue,
      ["@elements"] = ConvertChildren(model, node),
      ["ifc_type"] = node.Type,
      ["expressID"] = node.Id,
    };
  }

  private static Dictionary<string, object?> ConvertPropertySets(IfcNode node)
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

  public static Dictionary<string, object?> ToSpeckleDictionary(IfcPropSet ps)
  {
    var d = new Dictionary<string, object?>();
    foreach (var p in ps.GetProperties())
      d[p.Name] = p.Value.ToJsonObject();
    return d;
  }
}
