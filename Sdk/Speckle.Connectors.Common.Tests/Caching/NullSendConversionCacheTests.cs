using FluentAssertions;
using NUnit.Framework;
using Speckle.Connectors.Common.Caching;
using Speckle.Sdk.Models;
using Speckle.Sdk.Serialisation;

namespace Speckle.Connectors.Common.Tests.Caching;

public class NullSendConversionCacheTests
{
  [Test]
  public void Store()
  {
    var cache = new NullSendConversionCache();
    var projectId = "projectId";
    var id = new Id("id");
    var objectReference = new ObjectReference() { referencedId = "referencedId" };
    var convertedReferences = new Dictionary<Id, ObjectReference>() { { id, objectReference } };

    cache.StoreSendResult(projectId, convertedReferences);

    cache.TryGetValue(projectId, id.Value, out ObjectReference? _).Should().BeFalse();

    cache.ClearCache();

    cache.TryGetValue(projectId, id.Value, out _).Should().BeFalse();
  }

  [Test]
  public void Evict()
  {
    var cache = new NullSendConversionCache();
    var projectId = "projectId";
    var id = new Id("id");
    var objectReference = new ObjectReference() { referencedId = "referencedId" };
    var convertedReferences = new Dictionary<Id, ObjectReference>() { { id, objectReference } };

    cache.StoreSendResult(projectId, convertedReferences);

    cache.TryGetValue(projectId, id.Value, out ObjectReference? _).Should().BeFalse();

    cache.EvictObjects([id.Value]);

    cache.TryGetValue(projectId, id.Value, out _).Should().BeFalse();
  }
}
