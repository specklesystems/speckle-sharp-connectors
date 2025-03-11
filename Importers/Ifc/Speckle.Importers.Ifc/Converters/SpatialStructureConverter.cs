using Speckle.Importers.Ifc.Ara3D.IfcParser.Schema;
using Speckle.Importers.Ifc.Types;
using Speckle.InterfaceGenerator;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Importers.Ifc.Converters;

[GenerateAutoInterface]
public sealed class IfcSpatialStructureElementConverter : IIfcSpatialStructureElementConverter
{
  public Collection Convert(IfcModel model, IfcSpatialStructureElement node, INodeConverter childrenConverter)
  {
    if (!node.IsIfcRoot) //I'd really rather have a class for this (IfcRoot : IfcNode)
      throw new ArgumentException("Expected to be an IfcRoot", paramName: nameof(node));

    return new Collection
    {
      name = node.Name ?? node.Guid,
      applicationId = node.Guid,
      elements = childrenConverter.ConvertChildren(model, node),
      ["expressID"] = node.Id,
      ["ownerId"] = node.OwnerId,
      ["ifcType"] = node.Type,
      ["description"] = node.Description,
      ["objectType"] = node.ObjectType,
      ["compositionType"] = node.CompositionType,
      ["longName"] = node.LongName,
      ["properties"] = node.ConvertPropertySets(),
    };
  }
}
