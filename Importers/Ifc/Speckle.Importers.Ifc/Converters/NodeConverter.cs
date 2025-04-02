using Speckle.Importers.Ifc.Ara3D.IfcParser.Schema;
using Speckle.Importers.Ifc.Types;
using Speckle.InterfaceGenerator;
using Speckle.Sdk.Models;

namespace Speckle.Importers.Ifc.Converters;

/// <summary>
/// This is the main "recursive" converter for converting all IfcTypes to Speckle
/// </summary>
[GenerateAutoInterface]
public sealed class NodeConverter(
  IDataObjectConverter dataObjectConverter,
  IIfcSpatialStructureElementConverter spatialStructureConverter,
  IProjectConverter projectConverter
) : INodeConverter
{
  /// <summary>
  /// Converts Ifc nodes that inherits IfcRoot class To Speckle)
  /// </summary>
  /// <param name="model"></param>
  /// <param name="node"></param>
  /// <returns></returns>
  public Base Convert(IfcModel model, IfcNode node)
  {
    if (!node.IsIfcRoot)
      throw new ArgumentException("Expected to be an IfcRoot", paramName: nameof(node));

    return node switch
    {
      IfcProject project => projectConverter.Convert(model, project, this),
      //Note: we're only expecting IfcSite, IfcBuilding, and IfcBuildingStory's here...
      //but I cba to add full classes + inheritance, so IfcSpatialStructureElements is the closest common class
      IfcSpatialStructureElement structure => spatialStructureConverter.Convert(model, structure, this),
      IfcPropSet => throw new NotImplementedException("We didn't expect IfcPropSets here!"),
      _ => dataObjectConverter.Convert(model, node, this)
    };
  }

  public IEnumerable<Base> ConvertChildren(IfcModel model, IfcNode node)
  {
    return node.GetChildren().Where(x => x.IsIfcRoot).Select(x => Convert(model, x));
  }
}
