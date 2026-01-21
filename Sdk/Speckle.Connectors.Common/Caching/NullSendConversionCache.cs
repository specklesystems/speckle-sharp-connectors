using System.Diagnostics.CodeAnalysis;
using Speckle.Sdk.Models;
using Speckle.Sdk.Serialisation;

namespace Speckle.Connectors.Common.Caching;

/// <summary>
/// A null send conversion cache for future use in connectors that cannot support <see cref="ISendConversionCache"/>. It does nothing!
/// </summary>
public class NullSendConversionCache : ISendConversionCache
{
  public void StoreSendResult(string projectId, IReadOnlyDictionary<Id, ObjectReference> convertedReferences) { }

  public void AppendSendResult(string projectId, string applicationId, ObjectReference convertedReference) { }

  public void EvictObjects(IEnumerable<string> objectIds) { }

  public void ClearCache() { }

  public bool TryGetValue(
    string projectId,
    string applicationId,
    [NotNullWhen(true)] out ObjectReference? objectReference
  )
  {
    objectReference = null;
    return false;
  }
}
