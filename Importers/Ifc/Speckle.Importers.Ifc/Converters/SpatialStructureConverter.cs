using Speckle.Importers.Ifc.Ara3D.IfcParser.Schema;
using Speckle.Importers.Ifc.Types;
using Speckle.InterfaceGenerator;
using Speckle.Objects.Data;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Importers.Ifc.Converters;

[GenerateAutoInterface]
public sealed class IfcSpatialStructureElementConverter(IGeometryConverter geometryConverter)
  : IIfcSpatialStructureElementConverter
{
  public Collection Convert(IfcModel model, IfcSpatialStructureElement node, INodeConverter childrenConverter)
  {
    var directGeometry = ConvertAsDataObject(model, node);

    var relationalChildren = childrenConverter.ConvertChildren(model, node);
    var allChildren = relationalChildren.Prepend(directGeometry).ToList();

    //We're preferring to keep IFC collections lightweight, and adding a DataObject with the properties
    // 1. Spatial elements can can have direct geometry (mostly only common with IFC Site)
    // 2. Keeps property access simpler
    return new Collection
    {
      name = node.Name ?? node.LongName ?? node.Guid,
      elements = allChildren,
      ["expressID"] = node.Id,
    };
  }

  private DataObject ConvertAsDataObject(IfcModel model, IfcSpatialStructureElement node)
  {
    var geo = model.GetGeometry(node.Id);
    List<Base> displayValue = geo != null ? geometryConverter.Convert(geo) : new();

    return new DataObject
    {
      ["expressID"] = node.Id,
      ["ownerId"] = node.OwnerId,
      ["ifcType"] = node.Type,
      ["description"] = node.Description,
      ["objectType"] = node.ObjectType,
      ["compositionType"] = node.CompositionType,
      ["longName"] = node.LongName,
      name = node.Name ?? node.LongName ?? node.Guid,
      applicationId = node.Guid,
      properties = node.ConvertPropertySets(),
      displayValue = displayValue,
    };
  }
}
