using System.Diagnostics.CodeAnalysis;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;
using Speckle.Sdk.Serialisation;

namespace Speckle.Connectors.Common.Caching;

public readonly record struct SendCacheItem(string ApplicationId, string ProjectId);

/// <summary>
/// <para>Stores object references resulting from a send operation. These can be retrieved back during a subsequent send operation to bypass conversion if
/// they have not been changed. On large sends with small changes, this makes the process much speedier!</para>
/// <para>Note: do not and should not persist between file opens, should just persist in memory between send operations. Always eagerly invalidate.</para>
/// <para>If you ever implement a different conversion cache, do remember that objects in speckle are namespaced to each project (stream). E.g., if you send A to project C and project D, A needs to exist twice in the db. As such, always namespace stored references by project id.</para>
/// <para>Further note: Caching is optional in the send ops; an instance of this should be injected only in applications <b>where we know we can rely on change tracking!</b></para>
/// </summary>
public class SendConversionCache : ISendConversionCache
{
  private Dictionary<SendCacheItem, ObjectReference> Cache { get; set; } = new(); // NOTE: as this dude's accessed from potentially more operations at the same time, it might be safer to bless him as a concurrent dictionary.

  public void StoreSendResult(string projectId, IReadOnlyDictionary<Id, ObjectReference> convertedReferences)
  {
    foreach (var kvp in convertedReferences.Where(x => x.Value.applicationId != null))
    {
      Cache[new (kvp.Value.applicationId.NotNull(), projectId)] = kvp.Value;
    }
  }

  /// <summary>
  /// <para>Call this method whenever you need to invalidate a set of objects that have changed in the host app.</para>
  /// <para><b>Failure to do so correctly will result in cache poisoning and incorrect version creation (stale objects).</b></para>
  /// </summary>
  /// <param name="objectIds"></param>
  public void EvictObjects(IEnumerable<string> objectIds) =>
    Cache = Cache
      .Where(kvp => !objectIds.Contains(kvp.Key.ApplicationId))
      .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

  public void ClearCache() => Cache.Clear();

  public bool TryGetValue(
    string projectId,
    string applicationId,
    [NotNullWhen(true)] out ObjectReference? objectReference
  ) => Cache.TryGetValue(new(applicationId, projectId), out objectReference);
}
