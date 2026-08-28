using Speckle.Sdk;
using TSD.API.Remoting.Solver;

namespace Speckle.Converters.TSDShared.Results;

/// <summary>
/// Translates solver 1D element indices into the model entity ids that the published objects are interned under.
/// </summary>
/// <remarks>
/// The same shape of problem <see cref="TsdNodeIndexMap"/> solves for nodes. TSD numbers solver elements
/// independently of the entities they were meshed from, so the raw element index carried by a solver result does not
/// identify the member the result belongs to — and objects are published under <c>entity.Id</c>, so without this the
/// results have no join back to geometry at all. One member meshes into several solver elements, so this is
/// many-indices-to-one-entity; the caller keeps one row per element and simply points them at the same object.
/// Elements whose source entity is missing (an element with no model entity behind it) are absent from the map, and
/// the caller leaves those rows model-level rather than guessing.
/// </remarks>
public sealed class TsdElementIndexMap
{
  /// <summary>The tree keys whose entries are solver 1D element indices resolvable through this map.</summary>
  /// <remarks>
  /// Both come from <c>TsdElementForceResultsExtractorBase</c>. Scoped deliberately: nodal results are keyed by
  /// construction point index, which is a DIFFERENT index space that overlaps numerically, so resolving those through
  /// this map would attach a valid result to an unrelated member.
  /// </remarks>
  public static readonly string[] ElementKeyedResultTypes = { "elementEndForces", "offsetForces" };

  private readonly Dictionary<int, string> _entityIds;

  private TsdElementIndexMap(Dictionary<int, string> entityIds)
  {
    _entityIds = entityIds;
  }

  /// <summary>Reads the solver model's 1D elements and indexes each one's source entity id by element index.</summary>
  public static async Task<TsdElementIndexMap> CreateAsync(IModelBase solverModel, CancellationToken cancellationToken)
  {
    var elements = await solverModel.GetElements1DAsync(null, cancellationToken).ConfigureAwait(false);

    var entityIds = new Dictionary<int, string>();
    foreach (var element in elements ?? Enumerable.Empty<IElement1D>())
    {
      if (TryGetEntityId(element) is not Guid entityId || entityId == Guid.Empty)
      {
        continue;
      }

      entityIds[element.Index] = entityId.ToString();
    }

    return new TsdElementIndexMap(entityIds);
  }

  /// <summary>
  /// The published applicationId for a solver element index, or null when the element has no source entity.
  /// </summary>
  public string? GetEntityId(int solverElementIndex) =>
    _entityIds.TryGetValue(solverElementIndex, out var entityId) ? entityId : null;

  private static Guid? TryGetEntityId(IElement1D element)
  {
    try
    {
      return element.SourceSubEntityInfo.EntityId;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      // an element the remoting API cannot resolve a source for simply contributes nothing to the map
      return null;
    }
  }
}
