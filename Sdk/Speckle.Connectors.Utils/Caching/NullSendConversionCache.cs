using Speckle.Sdk.Models;

namespace Speckle.Connectors.Utils.Caching;

/// <summary>
/// A null send conversion cache for future use in connectors that cannot support <see cref="ISendConversionCache"/>. It does nothing!
/// </summary>
public class NullSendConversionCache : ISendConversionCache
{
  public void StoreSendResult(string projectId, IReadOnlyDictionary<string, ObjectReference> convertedReferences) { }

  public void EvictObjects(IEnumerable<string> objectIds) { }

  public void ClearCache() { }

  public bool TryGetValue(string projectId, string applicationId, out ObjectReference objectReference)
  {
    objectReference = new ObjectReference();
    return false;
  }
}