using Speckle.Importers.Ifc.Ara3D.IfcParser;
using Speckle.Importers.Ifc.Services;
using Speckle.Importers.Ifc.Types;
using Speckle.InterfaceGenerator;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Importers.Ifc.Converters;

[GenerateAutoInterface]
public class GraphConverter(INodeConverter nodeConverter, IRenderMaterialProxyManager proxyManager) : IGraphConverter
{
  public Base Convert(IfcModel model, IfcGraph graph)
  {
    var collection = new Collection();

    var children = graph.GetSources().Select(x => nodeConverter.Convert(model, x)).ToList();
    collection.elements = children;

    //Grabing materials from ProxyManager
    collection["renderMaterialProxies"] = proxyManager.RenderMaterialProxies.Values.ToList();

    return collection;
  }
}
