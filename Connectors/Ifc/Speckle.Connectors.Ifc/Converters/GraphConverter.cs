using Ara3D.IfcParser;
using Speckle.InterfaceGenerator;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.WebIfc.Importer.Ifc;

namespace Speckle.WebIfc.Importer.Converters;

[GenerateAutoInterface]
public class GraphConverter(INodeConverter nodeConverter) : IGraphConverter
{
  public Base Convert(IfcModel model, IfcGraph graph)
  {
    var collection = new Collection();
    var children = graph.GetSources().Select(x => nodeConverter.Convert(model, x)).ToList();
    collection.elements = children;
    return collection;
  }
}
