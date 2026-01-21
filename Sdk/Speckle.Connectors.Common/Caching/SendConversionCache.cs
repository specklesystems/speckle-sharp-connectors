using System.Diagnostics.CodeAnalysis;
using Speckle.Sdk.Models;
using Speckle.Sdk.Serialisation;

namespace Speckle.Connectors.Common.Caching;

///<inheritdoc/>
public class SendConversionCache : ISendConversionCache
{
  internal Dictionary<(string applicationId, string projectId), ObjectReference> Cache { get; set; } = new(); // NOTE: as this dude's accessed from potentially more operations at the same time, it might be safer to bless him as a concurrent dictionary.

  public void StoreSendResult(string projectId, IReadOnlyDictionary<Id, ObjectReference> convertedReferences)
  {
    foreach (var kvp in convertedReferences)
    {
      Cache[(kvp.Key.Value, projectId)] = kvp.Value;
    }
  }

  public void AppendSendResult(string projectId, string applicationId, ObjectReference convertedReference)
  {
    Cache[(applicationId, projectId)] = convertedReference;
  }

  /// <inheritdoc/>
  public void EvictObjects(IEnumerable<string> objectIds) =>
    Cache = Cache
      .Where(kvp => !objectIds.Contains(kvp.Key.applicationId))
      .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

  public void ClearCache() => Cache.Clear();

  public bool TryGetValue(
    string projectId,
    string applicationId,
    [NotNullWhen(true)] out ObjectReference? objectReference
  ) => Cache.TryGetValue((applicationId, projectId), out objectReference);
}
