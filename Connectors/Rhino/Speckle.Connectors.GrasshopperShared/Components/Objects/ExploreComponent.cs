using System.Collections;
using System.Globalization;
using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
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
      // Gate on there being input at all, not on it resolving. With no input the data simply hasn't arrived yet (file
      // load) and dropping ports would break wires - but input that resolves to nothing SHOULD clear them, otherwise
      // swapping to an object the graph doesn't know leaves the previous object's ports sitting there.
      if (Params.Input[0].VolatileData.DataCount > 0)
      {
        var resolved = Params
          .Input[0].VolatileData.AllData(true)
          .Select(Resolve)
          .Where(r => r is not null)
          .Cast<Dictionary<string, object?>>()
          .ToList();

        if (resolved.Count == 0)
        {
          // input arrived but none of it resolved - name what turned up, so an unhandled type is obvious rather
          // than looking like the component is broken
          var types = Params
            .Input[0].VolatileData.AllData(true)
            .Select(g => g.GetType().Name)
            .Distinct()
            .ToList();
          AddRuntimeMessage(
            GH_RuntimeMessageLevel.Remark,
            $"Nothing resolved from: {string.Join(", ", types)}."
          );
        }

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

    // read as IGH_Goo, not object - GetData<object> can hand back a cast of the goo rather than the goo itself
    IGH_Goo? item = null;
    if (!da.GetData(0, ref item) || item is null)
    {
      // the last silent path: no message here means the component looks broken rather than empty
      AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Nothing arrived on the input to explore.");
      return;
    }

    var values = Resolve(item);
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
  private Dictionary<string, object?>? Resolve(object item)
  {
    // the Goo types are siblings, not a hierarchy - each is its own GH_Goo<T>, so a block instance does NOT match
    // SpeckleGeometryWrapperGoo even though its wrapper derives from SpeckleGeometryWrapper. Unwrap each explicitly.
    var wrapper = item switch
    {
      SpeckleWrapper direct => direct,
      SpeckleCollectionWrapperGoo g => g.Value,
      SpeckleBlockInstanceWrapperGoo g => g.Value,
      SpeckleGeometryWrapperGoo g => g.Value,
      SpeckleDataObjectWrapperGoo g => g.Value,
      SpeckleBlockDefinitionWrapperGoo g => g.Value,
      _ => null,
    };

    if (wrapper is null)
    {
      return Explain(
        $"Can't explore a {item.GetType().Name}. Pipe in a Speckle Geometry, Block Instance or Collection."
      );
    }

    if (wrapper.ModelContext is not { } context)
    {
      return Explain("This object wasn't loaded from Speckle, so there is no graph to explore.");
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

    var values = wrapper switch
    {
      SpeckleGeometryWrapper { ObjectIndex: { } objK } => ResolveObject(graph, objK),
      // no object index means it didn't come out of a bundle - don't fall through to the model-scoped branch and
      // hand back levels for an object the graph doesn't know
      SpeckleGeometryWrapper => Explain(
        "This object has no graph reference. It came from inside a block definition, or from a 3.0 load."
      ),
      SpeckleCollectionWrapper => ResolveModel(graph),
      _ => Explain($"Nothing to explore for a {wrapper.GetType().Name}."),
    };

    if (values is { Count: 0 })
    {
      return Explain("The graph has nothing recorded against this object.");
    }

    return values;
  }

  /// <summary>Says why there is nothing to show. Grasshopper de-duplicates identical messages, so this is safe per item.</summary>
  private Dictionary<string, object?>? Explain(string message)
  {
    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, message);
    return null;
  }

  /// <summary>Relations hanging off one object.</summary>
  private static Dictionary<string, object?> ResolveObject(ArtefactGraph graph, int objK)
  {
    var bundle = graph.Bundle;
    var values = new Dictionary<string, object?>(StringComparer.Ordinal);

    // Every relation the bundle declares, both ways round. Nothing here knows what any of them mean - the catalog
    // supplies the name and which namespace each end lives in, so a relation added to the spec appears by itself.
    foreach (var type in graph.RelationTypes)
    {
      if (ArtefactGraphCache.IsObjectNamespace(type.SourceNamespace))
      {
        Add(values, Humanise(type.Name), Describe(graph.Targets(type.Rel, objK), type.TargetNamespace, bundle));
      }

      if (ArtefactGraphCache.IsObjectNamespace(type.TargetNamespace))
      {
        Add(
          values,
          $"{Humanise(type.Name)} (incoming)",
          Describe(graph.Sources(type.Rel, objK), type.SourceNamespace, bundle)
        );
      }
    }

    // The exceptions, all things the catalog can't give us. Elevation is the one genuinely numeric datum in the
    // graph; type parameters and sibling placements aren't relations at all.
    if (LevelNode(bundle, objK) is { Elevation: { } elevation } level)
    {
      Add(values, "Elevation", elevation * Units.GetConversionFactor(level.Units ?? bundle.Units, DocUnits()));
    }

    if (bundle.TypePropertiesByObject.TryGetValue(objK, out var typeProps) && typeProps.Count > 0)
    {
      Add(values, "Type Properties", new SpecklePropertyGroupGoo(typeProps));
    }

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

  // Used by the model-scoped branch, which reads the bundle's own maps rather than the catalog.
  private static string? NodeNameAt(ArtefactBundle bundle, int k) =>
    bundle.Nodes.TryGetValue(k, out var node) && node.Name is { Length: > 0 } name ? name
    : bundle.ObjectAppIds.TryGetValue(k, out var appId) ? appId
    : null;

  /// <summary>
  /// Turns dense ids into something readable, using the namespace the catalog declared for that end. Object and node
  /// ids overlap numerically, so guessing without the namespace would silently mislabel targets.
  /// </summary>
  private static List<string> Describe(IReadOnlyList<int> ks, string ns, ArtefactBundle bundle) =>
    ks.Select(k => Describe(k, ns, bundle)).Distinct().ToList();

  private static string Describe(int k, string ns, ArtefactBundle bundle)
  {
    if (ArtefactGraphCache.IsNodeNamespace(ns) && bundle.Nodes.TryGetValue(k, out var node))
    {
      return node.Name is { Length: > 0 } name ? name : $"node {k}";
    }
    if (ArtefactGraphCache.IsObjectNamespace(ns) && bundle.ObjectAppIds.TryGetValue(k, out var appId))
    {
      return appId;
    }
    return k.ToString(CultureInfo.InvariantCulture);
  }

  /// <summary>IN_COLLECTION -> "In Collection". The catalog names are spec constants; these are port labels.</summary>
  private static string Humanise(string specName) =>
    string.Join(
      " ",
      specName
        .Split('_')
        .Where(part => part.Length > 0)
        .Select(part => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant())
    );

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

  // Only ever called when OutputMismatch said so, and that check is what stops the loop: once the ports match, the
  // next solve won't schedule this again. Do NOT make the expire conditional on having changed something - if the
  // caller returned early without writing data and this then declines to expire, the component sits there stale.
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
