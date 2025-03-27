using Speckle.Importers.Ifc.Ara3D.IfcParser;
using Speckle.Importers.Ifc.Ara3D.IfcParser.Schema;
using Speckle.Importers.Ifc.Services;
using Speckle.Importers.Ifc.Types;
using Speckle.InterfaceGenerator;
using Speckle.Sdk;
using Speckle.Sdk.Models;

namespace Speckle.Importers.Ifc.Converters;

[GenerateAutoInterface]
public sealed class GraphConverter(
  INodeConverter nodeConverter,
  IUnitConverter unitConverter,
  IUnitContextManager unitContextManager,
  IRenderMaterialProxyManager proxyManager
) : IGraphConverter
{
  public Base Convert(IfcModel model, IfcGraph graph)
  {
    IfcProject project = graph.GetIfcProject();
    SetupUnitManager(project);

    Base rootCollection = nodeConverter.Convert(model, project);

    //Grabing materials from ProxyManager
    rootCollection["renderMaterialProxies"] = proxyManager.RenderMaterialProxies.Values.ToList();

    return rootCollection;
  }

  private void SetupUnitManager(IfcProject node)
  {
    if (node.UnitsInContext?.Units is not { } contextUnits)
    {
      throw new SpeckleException("Project does not specify a unit system");
    }

    IfcSiUnit length = FindUnitOfType("LENGTHUNIT", contextUnits);

    string units = unitConverter.Convert(length);

    unitContextManager.Units = units;
  }

  private static IfcSiUnit FindUnitOfType(string unitType, IEnumerable<IfcNode> node)
  {
    foreach (var unit in node)
    {
      if (unit is IfcSiUnit si)
      {
        if (si.UnitType == unitType)
        {
          return si;
        }
      }
    }

    throw new SpeckleException("Unsupported unit system - Only SI Units are supported");
  }
}
