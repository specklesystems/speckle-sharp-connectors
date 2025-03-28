using Speckle.Importers.Ifc.Ara3D.IfcParser.Schema;
using Speckle.Importers.Ifc.Types;
using Speckle.InterfaceGenerator;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Importers.Ifc.Converters;

[GenerateAutoInterface]
public sealed class ProjectConverter : IProjectConverter
{
  public Collection Convert(IfcModel model, IfcProject node, INodeConverter childrenConverter)
  {
    return new Collection
    {
      name = node.Name ?? node.Guid,
      applicationId = node.Guid,
      elements = childrenConverter.ConvertChildren(model, node).ToList(),
      ["expressID"] = node.Id,
      ["ownerId"] = node.OwnerId,
      ["ifcType"] = node.Type,
      ["description"] = node.Description,
      ["objectType"] = node.ObjectType,
      ["longName"] = node.LongName,
      ["phase"] = node.Phase,
      ["properties"] = node.ConvertPropertySets(),
    };
  }
}
