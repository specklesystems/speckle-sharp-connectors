using Speckle.Importers.Ifc.Ara3D.IfcParser.Schema;
using Speckle.Importers.Ifc.Types;
using Speckle.InterfaceGenerator;
using Speckle.Objects.Data;
using Speckle.Sdk.Models;

namespace Speckle.Importers.Ifc.Converters;

[GenerateAutoInterface]
public sealed class DataObjectConverter(IGeometryConverter geometryConverter) : IDataObjectConverter
{
  public DataObject Convert(IfcModel model, IfcNode node, INodeConverter childrenConverter)
  {
    // Even if there is no geometry, this will return an empty collection.
    var geo = model.GetGeometry(node.Id);
    List<Base> displayValue = geo != null ? geometryConverter.Convert(geo) : new();

    return new DataObject()
    {
      applicationId = node.Guid, // Guid is null for property values, and other Ifc entities not derived from IfcRoot
      properties = node.ConvertPropertySets(),
      name = node.Name ?? node.Guid,
      displayValue = displayValue,
      ["@elements"] = childrenConverter.ConvertChildren(model, node).ToList(),
      ["ifcType"] = node.Type,
      ["expressID"] = node.Id,
      ["ownerId"] = node.OwnerId,
      ["description"] = node.Description,
    };
  }
}
