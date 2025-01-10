using System.IO;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Rhino;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Exceptions;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.Rhino.Filters;
using Speckle.HostApps;
using Speckle.Newtonsoft.Json.Linq;
using Speckle.Sdk.SQLite;
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

    
    var testFactory = (TestSqLiteJsonCacheManagerFactory)serviceProvider.GetRequiredService<ISqLiteJsonCacheManagerFactory>();
    var fileName = Path.GetTempFileName();
    testFactory.Initialize(fileName);
    var send = serviceProvider.GetBinding<ISendBinding>();

    await send.Send(MODEL_CARD_ID);
    var sqLiteJsonCacheManager = testFactory.CreateFromStream(string.Empty);
    var all = sqLiteJsonCacheManager.GetAllObjects();
    var jObject = new JObject();
    foreach (var item in all)
    {
      jObject[item.Id] = item.Json;
    }
    Console.WriteLine(jObject.ToString());
  //  Snapshot.Match(jObject);

    if (File.Exists(fileName))
    {
      testFactory.Dispose();
      File.Delete(fileName);
    }
  }
}
