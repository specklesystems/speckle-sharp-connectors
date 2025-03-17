using FluentAssertions;
using NUnit.Framework;
using Speckle.Connectors.Common.Caching;
using Speckle.Sdk.Models;
using Speckle.Sdk.Serialisation;

namespace Speckle.Connectors.Common.Tests.Caching;

public class SendConversionCacheTests
{
  [Test]
  public void Store()
  {
    var cache = new SendConversionCache();
    var projectId = "projectId";
    var id = new Id("id");
    var objectReference = new ObjectReference() { referencedId = "referencedId" };
    var convertedReferences = new Dictionary<Id, ObjectReference>() { { id, objectReference } };

    cache.StoreSendResult(projectId, convertedReferences);
    cache.Cache.Count.Should().Be(1);

    cache.TryGetValue(projectId, id.Value, out ObjectReference? reference).Should().BeTrue();
    reference.Should().Be(objectReference);

    cache.ClearCache();
    cache.Cache.Count.Should().Be(0);

    cache.TryGetValue(projectId, id.Value, out _).Should().BeFalse();
  }

  [Test]
  public void Evict()
  {
    var cache = new SendConversionCache();
    var projectId = "projectId";
    var id = new Id("id");
    var objectReference = new ObjectReference() { referencedId = "referencedId" };
    var convertedReferences = new Dictionary<Id, ObjectReference>() { { id, objectReference } };

    cache.StoreSendResult(projectId, convertedReferences);
    cache.Cache.Count.Should().Be(1);

    cache.TryGetValue(projectId, id.Value, out ObjectReference? reference).Should().BeTrue();
    reference.Should().Be(objectReference);

    cache.EvictObjects([id.Value]);
    cache.Cache.Count.Should().Be(0);
  }
}
