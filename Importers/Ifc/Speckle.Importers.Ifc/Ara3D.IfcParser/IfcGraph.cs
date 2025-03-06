using System.Diagnostics;
using Ara3D.Logging;
using Ara3D.Utils;
using Speckle.Importers.Ifc.Ara3D.StepParser;

namespace Speckle.Importers.Ifc.Ara3D.IfcParser;

/// <summary>
/// This is a high-level representation of an IFC model as a graph of nodes and relations.
/// It also contains the  properties, and property sets.
/// </summary>
public sealed class IfcGraph
{
  public static IfcGraph Load(FilePath fp, ILogger? logger = null) =>
    new IfcGraph(new StepDocument(fp, logger), logger);

  public StepDocument Document { get; }

  public Dictionary<uint, IfcNode> Nodes { get; } = new Dictionary<uint, IfcNode>();
  public List<IfcRelation> Relations { get; } = new List<IfcRelation>();
  public Dictionary<uint, List<IfcRelation>> RelationsByNode { get; } = new Dictionary<uint, List<IfcRelation>>();
  public Dictionary<uint, List<IfcPropSet>> PropertySetsByNode { get; } = new Dictionary<uint, List<IfcPropSet>>();

  public uint IfcProjectId { get; }

  public IfcNode AddNode(IfcNode n) => Nodes[n.Id] = n;

  public IfcRelation AddRelation(IfcRelation r)
  {
    Relations.Add(r);
    RelationsByNode.Add(r.From.Id, r);
    return r;
  }

  public IfcGraph(StepDocument d, ILogger? logger = null)
  {
    Document = d;

    uint ifcProjectId = 0;
    logger?.Log("Computing entities");
    foreach (var inst in Document.RawInstances)
    {
      if (!inst.IsValid())
        continue;

      // TODO: converting entities into numerical hashes would likely improve performance significantly.
      // Here we are doing a lot of comparisons.

      // Property Values
      if (inst.Type.Equals("IFCPROPERTYSINGLEVALUE"))
      {
        var e = d.GetInstanceWithData(inst);
        AddNode(new IfcProp(this, e, e[2]));
      }
      else if (inst.Type.Equals("IFCPROPERTYENUMERATEDVALUE"))
      {
        var e = d.GetInstanceWithData(inst);
        AddNode(new IfcProp(this, e, e[2]));
      }
      else if (inst.Type.Equals("IFCPROPERTYREFERENCEVALUE"))
      {
        var e = d.GetInstanceWithData(inst);
        AddNode(new IfcProp(this, e, e[3]));
      }
      else if (inst.Type.Equals("IFCPROPERTYLISTVALUE"))
      {
        var e = d.GetInstanceWithData(inst);
        AddNode(new IfcProp(this, e, e[2]));
      }
      else if (inst.Type.Equals("IFCCOMPLEXPROPERTY"))
      {
        var e = d.GetInstanceWithData(inst);
        AddNode(new IfcProp(this, e, e[3]));
      }
      // Quantities which are a treated as a kind of prop
      // https://iaiweb.lbl.gov/Resources/IFC_Releases/R2x3_final/ifcquantityresource/lexical/ifcphysicalquantity.htm
      else if (inst.Type.Equals("IFCQUANTITYLENGTH"))
      {
        // https://iaiweb.lbl.gov/Resources/IFC_Releases/R2x3_final/ifcquantityresource/lexical/ifcquantitylength.htm
        var e = d.GetInstanceWithData(inst);
        AddNode(new IfcProp(this, e, e[3]));
      }
      else if (inst.Type.Equals("IFCQUANTITYAREA"))
      {
        // https://iaiweb.lbl.gov/Resources/IFC_Releases/R2x3_final/ifcquantityresource/lexical/ifcquantityarea.htm
        var e = d.GetInstanceWithData(inst);
        AddNode(new IfcProp(this, e, e[3]));
      }
      else if (inst.Type.Equals("IFCQUANTITYVOLUME"))
      {
        // https://iaiweb.lbl.gov/Resources/IFC_Releases/R2x3_final/ifcquantityresource/lexical/ifcquantityvolume.htm
        var e = d.GetInstanceWithData(inst);
        AddNode(new IfcProp(this, e, e[3]));
      }
      else if (inst.Type.Equals("IFCQUANTITYCOUNT"))
      {
        // https://iaiweb.lbl.gov/Resources/IFC_Releases/R2x3_final/ifcquantityresource/lexical/ifcquantitycount.htm
        var e = d.GetInstanceWithData(inst);
        AddNode(new IfcProp(this, e, e[3]));
      }
      else if (inst.Type.Equals("IFCQUANTITYWEIGHT"))
      {
        // https://iaiweb.lbl.gov/Resources/IFC_Releases/R2x3_final/ifcquantityresource/lexical/ifcquantityweight.htm
        var e = d.GetInstanceWithData(inst);
        AddNode(new IfcProp(this, e, e[3]));
      }
      else if (inst.Type.Equals("IFCQUANTITYTIME"))
      {
        // https://iaiweb.lbl.gov/Resources/IFC_Releases/R2x3_final/ifcquantityresource/lexical/ifcquantitytime.htm
        var e = d.GetInstanceWithData(inst);
        AddNode(new IfcProp(this, e, e[3]));
      }
      else if (inst.Type.Equals("IFCPHYSICALCOMPLEXQUANTITY"))
      {
        //https://iaiweb.lbl.gov/Resources/IFC_Releases/R2x3_final/ifcquantityresource/lexical/ifcphysicalcomplexquantity.htm
        var e = d.GetInstanceWithData(inst);
        AddNode(new IfcProp(this, e, e[2]));
      }
      // Property Set (or element quantity)
      else if (inst.Type.Equals("IFCPROPERTYSET"))
      {
        var e = d.GetInstanceWithData(inst);
        AddNode(new IfcPropSet(this, e, (StepList)e[4]));
      }
      else if (inst.Type.Equals("IFCELEMENTQUANTITY"))
      {
        var e = d.GetInstanceWithData(inst);
        AddNode(new IfcPropSet(this, e, (StepList)e[5]));
      }
      // Aggregate relation
      else if (inst.Type.Equals("IFCRELAGGREGATES"))
      {
        var e = d.GetInstanceWithData(inst);
        AddRelation(new IfcRelationAggregate(this, e, (StepId)e[4], (StepList)e[5]));
      }
      // Spatial relation
      else if (inst.Type.Equals("IFCRELCONTAINEDINSPATIALSTRUCTURE"))
      {
        var e = d.GetInstanceWithData(inst);
        AddRelation(new IfcRelationSpatial(this, e, (StepId)e[5], (StepList)e[4]));
      }
      // Property set relations
      else if (inst.Type.Equals("IFCRELDEFINESBYPROPERTIES"))
      {
        var e = d.GetInstanceWithData(inst);
        AddRelation(new IfcPropSetRelation(this, e, (StepId)e[5], (StepList)e[4]));
      }
      // Type relations
      else if (inst.Type.Equals("IFCRELDEFINESBYTYPE"))
      {
        var e = d.GetInstanceWithData(inst);
        AddRelation(new IfcRelationType(this, e, (StepId)e[5], (StepList)e[4]));
      }
      // Everything else
      else
      {
        //Special case for IFC Projects, track them as a root node.
        if (inst.Type.Equals("IFCPROJECT"))
          ifcProjectId = inst.Id;

        // Simple IFC node: without step entity data.
        var e = d.GetInstanceWithData(inst);
        AddNode(new IfcNode(this, e));
      }
    }

    if (ifcProjectId <= 0)
      throw new SpeckleIfcException("There was no IfcProject in the file");

    IfcProjectId = ifcProjectId;

    logger?.Log("Creating lookup of property sets");

    foreach (var psr in Relations.OfType<IfcPropSetRelation>())
    {
      var ps = psr.PropSet;
      foreach (var id in psr.GetRelatedIds())
      {
        PropertySetsByNode.Add(id, ps);
      }
    }

    logger?.Log("Completed creating model graph");
  }

  public IEnumerable<IfcNode> GetNodes() => Nodes.Values;

  public IEnumerable<IfcNode> GetNodes(IEnumerable<uint> ids) => ids.Select(GetNode);

  public IfcNode GetOrCreateNode(StepInstance lineData, int arg)
  {
    if (arg < 0 || arg >= lineData.AttributeValues.Count)
      throw new SpeckleIfcException("Argument index out of range");
    return GetOrCreateNode(lineData.AttributeValues[arg]);
  }

  public IfcNode GetOrCreateNode(StepValue o) =>
    GetOrCreateNode(o is StepId id ? id.Id : throw new SpeckleIfcException($"Expected a StepId value, not {o}"));

  public IfcNode GetOrCreateNode(uint id)
  {
    var r = Nodes.TryGetValue(id, out var node) ? node : AddNode(new IfcNode(this, Document.GetInstanceWithData(id)));
    Debug.Assert(r.Id == id);
    return r;
  }

  public List<IfcNode> GetOrCreateNodes(List<StepValue> list) => list.Select(GetOrCreateNode).ToList();

  public List<IfcNode> GetOrCreateNodes(StepInstance line, int arg)
  {
    if (arg < 0 || arg >= line.AttributeValues.Count)
      throw new SpeckleIfcException("Argument out of range");
    if (line.AttributeValues[arg] is not StepList agg)
      throw new SpeckleIfcException("Expected a list");
    return GetOrCreateNodes(agg.Values);
  }

  public IfcNode GetNode(StepId id) => GetNode(id.Id);

  public IfcNode GetNode(uint id)
  {
    var r = Nodes[id];
    Debug.Assert(r.Id == id);
    return r;
  }

  public IfcNode GetIfcProject() => GetNode(IfcProjectId);

  public IEnumerable<IfcPropSet> GetPropSets() => GetNodes().OfType<IfcPropSet>();

  public IEnumerable<IfcProp> GetProps() => GetNodes().OfType<IfcProp>();

  public IEnumerable<IfcRelationSpatial> GetSpatialRelations() => Relations.OfType<IfcRelationSpatial>();

  public IEnumerable<IfcRelationAggregate> GetAggregateRelations() => Relations.OfType<IfcRelationAggregate>();

  public IReadOnlyList<IfcRelation> GetRelationsFrom(uint id) =>
    RelationsByNode.TryGetValue(id, out var list) ? list : Array.Empty<IfcRelation>();
}
