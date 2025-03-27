using Speckle.Importers.Ifc.Ara3D.IfcParser.Schema;
using Speckle.InterfaceGenerator;
using Speckle.Sdk;
using Speckle.Sdk.Common;

namespace Speckle.Importers.Ifc.Converters;

[GenerateAutoInterface]
public class UnitConverter : IUnitConverter
{
  public string Convert(IfcSiUnit node)
  {
    if (node.UnitName != "METRE")
      return node.Prefix switch
      {
        null => Units.Meters,
        "MILLI" => Units.Millimeters,
        "CENTI" => Units.Centimeters,
        //"DECI" => null,
        "KILO" => Units.Kilometers,
        _ => throw new SpeckleException($"Units {node.Prefix}{node.UnitName} are not a supported length unit")
      };

    throw new SpeckleException($"Units {node.Prefix}{node.UnitName} are not a supported length unit");
  }
}
