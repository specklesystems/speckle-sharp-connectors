using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Rhino;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Exceptions;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.DUI.Testing;
using Speckle.Connectors.Rhino.Filters;
using Speckle.HostApps;
using Speckle.Sdk.Api;
using Speckle.Sdk.Models;
using Speckle.Sdk.Serialisation.V2.Send;
using Speckle.Sdk.Transports;
using Xunit;

namespace Speckle.Connectors.Rhino;

[Collection(RhinoSetup.RhinoCollection)]
public class SendTests(IServiceProvider serviceProvider)
{
  private const string MODEL_CARD_ID = "modelCardId";
  
  //[Fact]
  private async Task Test_Send_Zero()
  {
    
    var binding = serviceProvider.GetBinding<IBasicConnectorBinding>();
    binding.AddModel(new SenderModelCard()
    {
      ModelCardId = MODEL_CARD_ID,
      SendFilter = new RhinoSelectionFilter()
      {
        SelectedObjectIds  = new()
      }
    });
    
    var send = serviceProvider.GetBinding<ISendBinding>();
    await FluentActions.Invoking(async () => await send.Send(MODEL_CARD_ID)).Should()
      .ThrowAsync<SpeckleSendFilterException>();
  }
  
  [Fact]
  public async Task Test_Send_Current()
  {
    foreach (var currentDoc in RhinoDoc.OpenDocuments())
    {
      currentDoc.Dispose();
    }
    using var doc = RhinoDoc.Open("C:\\Users\\adam\\Git\\speckle-sharp-connectors\\Tests\\Models\\cube.3dm", out bool _);
    var ids = doc.Objects.Select(x => x.Id).ToList();
    ids.Should().NotBeEmpty();
    
    doc.Objects.Select(ids, true);
    
    var binding = serviceProvider.GetBinding<IBasicConnectorBinding>();
    binding.AddModel(new SenderModelCard()
    {
      ModelCardId = MODEL_CARD_ID,
      SendFilter = new RhinoSelectionFilter()
      {
        SelectedObjectIds  = ids.Select(x => x.ToString()).ToList()
      },
      AccountId = "AccountId",
      ServerUrl = "http://localhost/",
      ProjectId = "ProjectId",
      ModelId = "ModelId",
    });

    var testOperations = (TestOperations)serviceProvider.GetRequiredService<IOperations>();
    testOperations.WrappedOperations = new TestSender();
    
    var send = serviceProvider.GetBinding<ISendBinding>();

    await send.Send(MODEL_CARD_ID);
  }

  private sealed class TestSender : IOperations
  {
    public  Task<Base> Receive2(Uri url, string streamId, string objectId, string? authorizationToken = null,
      IProgress<ProgressArgs>? onProgressAction = null, CancellationToken cancellationToken = new CancellationToken()) =>
      throw new NotImplementedException();

    public  Task<Base> Receive(string objectId, ITransport? remoteTransport = null, ITransport? localTransport = null,
      IProgress<ProgressArgs>? onProgressAction = null, CancellationToken cancellationToken = new CancellationToken()) =>
      throw new NotImplementedException();

    public Task<SerializeProcessResults> Send2(Uri url, string streamId, string? authorizationToken, Base value,
      IProgress<ProgressArgs>? onProgressAction = null,
      CancellationToken cancellationToken = new())
    {
      return Task.FromResult(new SerializeProcessResults());
    }

    public  Task<(string rootObjId, IReadOnlyDictionary<string, ObjectReference> convertedReferences)> Send(Base value, IServerTransport transport, bool useDefaultCache, IProgress<ProgressArgs>? onProgressAction = null,
      CancellationToken cancellationToken = new CancellationToken()) =>
      throw new NotImplementedException();

    public  Task<(string rootObjId, IReadOnlyDictionary<string, ObjectReference> convertedReferences)> Send(Base value, ITransport transport, bool useDefaultCache, IProgress<ProgressArgs>? onProgressAction = null,
      CancellationToken cancellationToken = new CancellationToken()) =>
      throw new NotImplementedException();

    public  Task<(string rootObjId, IReadOnlyDictionary<string, ObjectReference> convertedReferences)> Send(Base value, IReadOnlyCollection<ITransport> transports, IProgress<ProgressArgs>? onProgressAction = null,
      CancellationToken cancellationToken = new CancellationToken()) =>
      throw new NotImplementedException();

    public string Serialize(Base value, CancellationToken cancellationToken = new CancellationToken()) => throw new NotImplementedException();

    public  Task<Base> DeserializeAsync(string value, CancellationToken cancellationToken = new CancellationToken()) => throw new NotImplementedException();
  }
}
