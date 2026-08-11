using System.Collections;
using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Rhino;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Operations.Receive;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Pipelines;
using Speckle.Sdk.Pipelines.Receive.Artifacts;
using RG = Rhino.Geometry;

namespace Speckle.Connectors.GrasshopperShared.Components.Objects;

/// <summary>
/// Surfaces what the envelope graph knows about whatever is piped in - the outputs are whichever relations actually
/// resolve, so a Revit model and a Rhino model produce different ones.
/// </summary>
/// <remarks>
/// <para>Only covers what a wrapper does NOT already carry. Material, colour and collection path are resolved onto
/// the wrappers during receive, so repeating them here would be noise.</para>
/// <para>Object-to-object relations (host, subelements, connects-to, bounds, siblings) come out as application ids
/// rather than geometry: the graph knows the ids, but Explore only sees the item piped into it, not the rest of the
/// loaded tree. Pair with a lookup component to turn those back into objects.</para>
/// <para>Reads only the bundle a receive already cached. A legacy-loaded model, or one whose temp folder has been
/// cleared, reports no graph and asks for a reload.</para>
/// </remarks>
[Guid("92A14FC0-6725-47F7-9CDF-11D528FBB5B5")]
public class ExploreComponent : GH_Component, IGH_VariableParameterComponent
{
  public ExploreComponent()
    : base(
      "Explore",
      "Ex",
      "Discovers what the model graph knows about an object or collection",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.OBJECTS
    ) { }

  public override Guid ComponentGuid => GetType().GUID;
  protected override Bitmap Icon => Resources.speckle_objects_query;
  public override GH_Exposure Exposure => GH_Exposure.secondary;

  protected override void RegisterInputParams(GH_InputParamManager pManager) =>
    pManager.AddGenericParameter(
      "Speckle Object",
      "O",
      "A Speckle Geometry or Collection loaded from Speckle",
      GH_ParamAccess.item
    );

  protected override void RegisterOutputParams(GH_OutputParamManager pManager) { }

  protected override void SolveInstance(IGH_DataAccess da)
  {
    // Port generation runs on the first iteration over ALL the input, so the output set is the union of what resolved
    // anywhere. Per-item gaps then come out as empty branches, which is how Grasshopper already reads "not
    // applicable". Mirrors ExpandSpeckleProperties.
    if (da.Iteration == 0)
    {
      var resolved = Params
        .Input[0].VolatileData.AllData(true)
        .Select(Resolve)
        .Where(r => r is not null)
        .Cast<Dictionary<string, object?>>()
        .ToList();

      if (resolved.Count > 0)
      {
        var names = new List<string>();
        foreach (var name in resolved.SelectMany(r => r.Keys))
        {
          if (!names.Contains(name))
          {
            names.Add(name);
          }
        }

        if (OutputMismatch(names))
        {
          OnPingDocument()?.ScheduleSolution(5, _ => CreateOutputs(names, resolved));
          return;
        }
      }
    }

    object? item = null;
    da.GetData(0, ref item);
    var values = item is null ? null : Resolve(item);
    if (values is null)
    {
      return;
    }

    for (int i = 0; i < Params.Output.Count; i++)
    {
      if (!values.TryGetValue(Params.Output[i].Name, out var value) || value is null)
      {
        continue;
      }

      if (Params.Output[i].Access == GH_ParamAccess.list)
      {
        da.SetDataList(i, value as IList ?? new List<object?> { value });
      }
      else
      {
        da.SetData(i, value);
      }
    }
  }

  /// <summary>
  /// Everything the graph can say about one piped-in item, or null when it didn't come from a 4.0 receive.
  /// </summary>
  private Dictionary<string, object?>? Resolve(object? item)
  {
    var wrapper = item switch
    {
      SpeckleCollectionWrapperGoo collection => (SpeckleWrapper?)collection.Value,
      SpeckleGeometryWrapperGoo geometry => geometry.Value,
      SpeckleWrapper direct => direct,
      _ => null,
    };

    if (wrapper?.ModelContext is not { } context)
    {
      return null;
    }

    ArtefactGraph? graph;
    try
    {
      // no token to thread here - SolveInstance is synchronous on a plain GH_Component
      graph = ArtefactGraphCache.TryGet(context.VersionId, CancellationToken.None);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Could not read the model graph ({ex.Message}).");
      return null;
    }

    if (graph is null)
    {
      AddRuntimeMessage(
        GH_RuntimeMessageLevel.Remark,
        "No 4.0 graph is cached for this model - it was loaded from a 3.0 version, or the cache has been cleared. "
          + "Reload the model to explore it."
      );
      return null;
    }

    return wrapper is SpeckleGeometryWrapper { ObjectIndex: { } objK }
      ? ResolveObject(graph, objK)
      : ResolveModel(graph);
  }

  /// <summary>Relations hanging off one object.</summary>
  private static Dictionary<string, object?> ResolveObject(ArtefactGraph graph, int objK)
  {
    var bundle = graph.Bundle;
    var values = new Dictionary<string, object?>(StringComparer.Ordinal);

    if (LevelNode(bundle, objK) is { } level)
    {
      Add(values, "Level", level.Name);
      if (level.Elevation is { } elevation)
      {
        Add(values, "Elevation", elevation * Units.GetConversionFactor(level.Units ?? bundle.Units, DocUnits()));
      }
    }

    Add(values, "Model", NodeName(bundle, RelKind.InModel, objK));
    Add(values, "Room", NodeName(bundle, RelKind.InRoom, objK));
    Add(values, "System", NodeName(bundle, RelKind.InSystem, objK));

    if (bundle.Relations.GroupsByObject.TryGetValue(objK, out var groupKs))
    {
      Add(values, "Groups", groupKs.Select(k => NodeNameAt(bundle, k)).Where(n => n is not null).ToList());
    }

    // object-to-object relations resolve to application ids - see the class remarks
    Add(values, "Host", AppIds(graph, RelKind.HostedOn, objK).FirstOrDefault());
    Add(values, "Subelements", AppIds(graph, RelKind.Subelement, objK));
    Add(values, "Connects To", AppIds(graph, RelKind.ConnectsTo, objK));
    Add(values, "Bounds", AppIds(graph, RelKind.Bounds, objK));
    Add(values, "Siblings", Siblings(bundle, objK));

    return values;
  }

  /// <summary>Model-scoped things, for a collection. Levels come from the nodes directly, so empty ones still show.</summary>
  private static Dictionary<string, object?> ResolveModel(ArtefactGraph graph)
  {
    var bundle = graph.Bundle;
    var values = new Dictionary<string, object?>(StringComparer.Ordinal);

    var levels = bundle
      .Nodes.Values.Where(n => n.Kind == NodeKind.Level)
      .Select(n => (n.Name, Elevation: n.Elevation * Units.GetConversionFactor(n.Units ?? bundle.Units, DocUnits())))
      .OrderBy(l => l.Elevation ?? 0)
      .ToList();

    if (levels.Count > 0)
    {
      Add(values, "Levels", levels.Select(l => l.Name).ToList());
      Add(values, "Elevations", levels.Select(l => l.Elevation).ToList());
      // a plane per level is what people otherwise rebuild by hand to section against
      Add(
        values,
        "Level Planes",
        levels.Select(l => new RG.Plane(new RG.Point3d(0, 0, l.Elevation ?? 0), RG.Vector3d.ZAxis)).ToList()
      );
    }

    var models = bundle
      .Relations.ObjectNodeByRel.TryGetValue(RelKind.InModel, out var byObject)
      ? byObject.Values.Distinct().Select(k => NodeNameAt(bundle, k)).Where(n => n is not null).ToList()
      : [];
    Add(values, "Models", models);

    return values;
  }

  private static ArtefactNode? LevelNode(ArtefactBundle bundle, int objK) =>
    bundle.Relations.ObjectNodeByRel.TryGetValue(RelKind.OnLevel, out var byObject)
    && byObject.TryGetValue(objK, out var nodeK)
    && bundle.Nodes.TryGetValue(nodeK, out var node)
      ? node
      : null;

  private static string? NodeName(ArtefactBundle bundle, byte rel, int objK) =>
    bundle.Relations.ObjectNodeByRel.TryGetValue(rel, out var byObject) && byObject.TryGetValue(objK, out var nodeK)
      ? NodeNameAt(bundle, nodeK)
      : null;

  // IN_ROOM and friends are object-to-object in the spec but the SDK files them with the object-to-node relations, so
  // the target may be either. Try a node first, then fall back to an object's application id.
  private static string? NodeNameAt(ArtefactBundle bundle, int k) =>
    bundle.Nodes.TryGetValue(k, out var node) && node.Name is { Length: > 0 } name ? name
    : bundle.ObjectAppIds.TryGetValue(k, out var appId) ? appId
    : null;

  private static List<string> AppIds(ArtefactGraph graph, byte rel, int objK) =>
    graph
      .Targets(rel, objK)
      .Select(k => graph.Bundle.ObjectAppIds.TryGetValue(k, out var appId) ? appId : null)
      .Where(a => a is not null)
      .Cast<string>()
      .ToList();

  /// <summary>Other objects placing the same block definition as this one.</summary>
  private static List<string> Siblings(ArtefactBundle bundle, int objK)
  {
    if (!bundle.Relations.DisplayInstanceByObject.TryGetValue(objK, out var instanceK))
    {
      return [];
    }
    if (!bundle.Nodes.TryGetValue(instanceK, out var instance) || instance.DefRef is not int defK)
    {
      return [];
    }

    return bundle
      .Relations.DisplayInstanceEdges.Where(e =>
        e.Src != objK && bundle.Nodes.TryGetValue(e.Dst, out var n) && n.DefRef == defK
      )
      .Select(e => bundle.ObjectAppIds.TryGetValue(e.Src, out var appId) ? appId : null)
      .Where(a => a is not null)
      .Cast<string>()
      .Distinct()
      .ToList();
  }

  private static void Add(Dictionary<string, object?> values, string name, object? value)
  {
    if (value is null || (value is ICollection collection && collection.Count == 0))
    {
      return;
    }
    values[name] = value;
  }

  private static string DocUnits() => RhinoDoc.ActiveDoc?.ModelUnitSystem.ToSpeckleString() ?? Units.Meters;

  private bool OutputMismatch(List<string> names)
  {
    if (Params.Output.Count != names.Count)
    {
      return true;
    }
    return names.Where((t, i) => Params.Output[i].Name != t).Any();
  }

  private void CreateOutputs(List<string> names, List<Dictionary<string, object?>> resolved)
  {
    for (int i = Params.Output.Count - 1; i >= 0; i--)
    {
      if (!names.Contains(Params.Output[i].Name))
      {
        Params.UnregisterOutputParameter(Params.Output[i]);
      }
    }

    for (int i = 0; i < names.Count; i++)
    {
      // list access when the relation came out multi-valued anywhere in the input
      var access = resolved.Any(r => r.TryGetValue(names[i], out var v) && v is IList)
        ? GH_ParamAccess.list
        : GH_ParamAccess.item;

      var existing = Params.Output.FirstOrDefault(p => p.Name == names[i]);
      if (existing is null)
      {
        Params.RegisterOutputParam(
          new SpeckleOutputParam
          {
            Name = names[i],
            NickName = names[i],
            MutableNickName = false,
            Access = access,
          },
          i
        );
        continue;
      }

      existing.Access = access;
      int current = Params.Output.IndexOf(existing);
      if (current != i)
      {
        Params.Output.RemoveAt(current);
        Params.Output.Insert(i, existing);
      }
    }

    Params.OnParametersChanged();
    VariableParameterMaintenance();
    ExpireSolution(false);
  }

  public bool CanInsertParameter(GH_ParameterSide side, int index) => false;

  public bool CanRemoveParameter(GH_ParameterSide side, int index) => false;

  public IGH_Param CreateParameter(GH_ParameterSide side, int index) => new SpeckleOutputParam();

  public bool DestroyParameter(GH_ParameterSide side, int index) => side == GH_ParameterSide.Output;

  public void VariableParameterMaintenance() { }
}
