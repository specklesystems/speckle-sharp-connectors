using Speckle.Sdk.Api;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;
using Speckle.Sdk.Serialisation.V2.Send;
using Speckle.Sdk.Transports;
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

namespace Speckle.Connectors.DUI.Testing;

public class TestOperations : IOperations 
{
  public       IOperations? WrappedOperations { get; set; } = null!;
  public async Task<Base> Receive2(Uri url, string streamId, string objectId, string? authorizationToken = null,
    IProgress<ProgressArgs>? onProgressAction = null, CancellationToken cancellationToken = new CancellationToken()) =>
    throw new NotImplementedException();

  public async Task<Base> Receive(string objectId, ITransport? remoteTransport = null, ITransport? localTransport = null,
    IProgress<ProgressArgs>? onProgressAction = null, CancellationToken cancellationToken = new CancellationToken()) =>
    throw new NotImplementedException();

  public Task<SerializeProcessResults> Send2(Uri url, string streamId, string? authorizationToken, Base value, IProgress<ProgressArgs>? onProgressAction = null,
    CancellationToken cancellationToken = new CancellationToken()) =>
    WrappedOperations.NotNull().Send2(url, streamId, authorizationToken, value, onProgressAction, cancellationToken);

  public async Task<(string rootObjId, IReadOnlyDictionary<string, ObjectReference> convertedReferences)> Send(Base value, IServerTransport transport, bool useDefaultCache, IProgress<ProgressArgs>? onProgressAction = null,
    CancellationToken cancellationToken = new CancellationToken()) =>
    throw new NotImplementedException();

  public async Task<(string rootObjId, IReadOnlyDictionary<string, ObjectReference> convertedReferences)> Send(Base value, ITransport transport, bool useDefaultCache, IProgress<ProgressArgs>? onProgressAction = null,
    CancellationToken cancellationToken = new CancellationToken()) =>
    throw new NotImplementedException();

  public async Task<(string rootObjId, IReadOnlyDictionary<string, ObjectReference> convertedReferences)> Send(Base value, IReadOnlyCollection<ITransport> transports, IProgress<ProgressArgs>? onProgressAction = null,
    CancellationToken cancellationToken = new CancellationToken()) =>
    throw new NotImplementedException();

  public string Serialize(Base value, CancellationToken cancellationToken = new CancellationToken()) => throw new NotImplementedException();

  public async Task<Base> DeserializeAsync(string value, CancellationToken cancellationToken = new CancellationToken()) => throw new NotImplementedException();
}
