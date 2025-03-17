using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Speckle.Importers.Ifc.Ara3D.StepParser;
using Speckle.Sdk.Common;

namespace Speckle.Importers.Ifc.Ara3D.IfcParser.Schema;

// This merges two separate entity types: IfcPropertySet and IfcElementQuantity.
// Both of which are derived from IfcPropertySetDefinition.
// This is something that can be referred to by a PropertySetRelation
// An IfcElementQuantity has an additional "method of measurement" property.
// https://standards.buildingsmart.org/IFC/RELEASE/IFC2x3/TC1/HTML/ifckernel/lexical/ifcpropertyset.htm
// https://standards.buildingsmart.org/IFC/RELEASE/IFC2x3/TC1/HTML/ifcproductextension/lexical/ifcelementquantity.htm
public class IfcPropSet : IfcNode
{
  public readonly StepList? PropertyIdList;

  public IfcPropSet(IfcGraph graph, StepInstance lineData, StepList? propertyIdList)
    : base(graph, lineData)
  {
    Debug.Assert(IsIfcRoot);
    Debug.Assert(lineData.AttributeValues.Count is 5 or 6);
    Debug.Assert(Type is "IFCPROPERTYSET" or "IFCELEMENTQUANTITY");
    PropertyIdList = propertyIdList;
  }

  public IEnumerable<IfcProp> GetProperties()
  {
    for (var i = 0; i < NumProperties; ++i)
    {
      var id = PropertyId(i);
      var node = Graph.GetNode(id);
      if (node is not IfcProp prop)
        throw new SpeckleIfcException($"Expected a property not {node} from id {id}");
      yield return prop;
    }
  }

  public bool IsQuantity => LineData.AttributeValues.Count == 6;
  public string? MethodOfMeasurement => IsQuantity ? this[4]?.AsString() : null;
  public int NumProperties => PropertyIdList?.Values.Count ?? 0;

  [MemberNotNull(nameof(PropertyIdList))]
  public uint PropertyId(int i) => PropertyIdList.NotNull().Values[i].AsId();
}
