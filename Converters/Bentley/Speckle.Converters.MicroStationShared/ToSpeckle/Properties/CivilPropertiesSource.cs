using Bentley.DgnPlatformNET.DgnEC;
using Bentley.ECObjects.Instance;

namespace Speckle.Converters.MicroStation.ToSpeckle.Properties;

/// <summary>Result of one element's property extraction.</summary>
/// <param name="Properties">The properties dictionary shipped on the MicrostationObject.</param>
/// <param name="IsCivil">True when the element carries Bentley Civil (OpenRoads) EC data — the
/// builder then adds computed "Civil Quantities" from the extracted meshes.</param>
public readonly record struct PropertiesResult(Dictionary<string, object?> Properties, bool IsCivil);

/// <summary>
/// OpenRoads / Bentley Civil property source — the managed counterpart of dgnextract's
/// <c>civil_dgn.h</c> aspect-graph walk. A civil element (corridor component mesh, terrain, 3D
/// linework, …) carries only internal flags on its own EC instances; the user-facing data the
/// OpenRoads properties panel shows lives on RELATED aspect instances reached through
/// Bentley_Civil__* relationship instances. This walks
/// <see cref="IECInstance.GetRelationshipInstances"/> breadth-first (depth ≤ 8, cycle-guarded) and
/// contributes every related civil instance's values, grouped by the instance's class display label.
/// <para>
/// Known gaps vs dgnextract (documented for the parity matrix): the feature NAME recorded by
/// name-index holder elements in other models, and the corridor identity parsed from XmlFragment
/// XAttributes, surface only if the DgnEC provider materialises those relationships; "Civil
/// Quantities" areas are computed by the builder from the extracted meshes instead.
/// </para>
/// </summary>
internal static class CivilPropertiesSource
{
  private const int MAX_GRAPH_DEPTH = 8;

  public static void Contribute(List<IDgnECInstance> seeds, Dictionary<string, object?> props)
  {
    var visited = new HashSet<string>();
    var queue = new Queue<(IECInstance Instance, int Depth)>();
    foreach (IDgnECInstance seed in seeds)
    {
      queue.Enqueue((seed, 0));
      MarkVisited(visited, seed);
    }

    while (queue.Count > 0)
    {
      var (instance, depth) = queue.Dequeue();
      if (depth > 0)
      {
        ContributeCivilInstance(instance, props);
      }
      if (depth >= MAX_GRAPH_DEPTH)
      {
        continue;
      }

      IECRelationshipInstanceCollection? relationships;
      try
      {
        relationships = instance.GetRelationshipInstances();
      }
      catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
      {
        continue;
      }
      if (relationships == null)
      {
        continue;
      }

      foreach (object? rel in relationships)
      {
        if (rel is not IECRelationshipInstance relationship)
        {
          continue;
        }
        foreach (IECInstance? related in new[] { relationship.Source, relationship.Target })
        {
          if (related == null || !MarkVisited(visited, related))
          {
            continue;
          }
          if (PropertiesExtractor.IsCivilSchema(related.ClassDefinition?.Schema?.Name))
          {
            queue.Enqueue((related, depth + 1));
          }
        }
      }
    }
  }

  private static void ContributeCivilInstance(IECInstance instance, Dictionary<string, object?> props)
  {
    var classDef = instance.ClassDefinition;
    if (classDef == null)
    {
      return;
    }
    Dictionary<string, object?>? values = PropertiesExtractor.ReadInstanceValues(instance);
    if (values == null)
    {
      return;
    }
    string groupName = PropertiesExtractor.DecodeEcName(
      classDef.IsDisplayLabelDefined ? classDef.DisplayLabel : classDef.Name ?? "Civil"
    );
    PropertiesExtractor.AddKeepingAll(props, groupName, values);
  }

  private static bool MarkVisited(HashSet<string> visited, IECInstance instance)
  {
    string key = $"{instance.ClassDefinition?.Name}|{instance.InstanceId}";
    return visited.Add(key);
  }
}
