using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Inputs;
using Speckle.Sdk.Api.GraphQL.Models;
using Speckle.Sdk.Common;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Models;

namespace Speckle.Importers.Ifc.Tester2;

public class Sender(IClientFactory clientFactory, IAccountManager accountManager, IOperations operations)
{
  public const string MODEL_NAME = "IFC Import";

  public async Task<string> Send(Base rootObject, string projectId)
  {
    var account = accountManager.GetDefaultAccount().NotNull();
    using var client = clientFactory.Create(account);

    var res = await operations.Send2(new(account.serverInfo.url), projectId, account.token, rootObject);

    var t = await GetOrCreateIfcModel(client, projectId);

    CreateVersionInput input = new(res.RootId, t.id, projectId);
    return await client.Version.Create(input);
  }

  public async Task<Model> GetOrCreateIfcModel(Client client, string projectId)
  {
    ProjectModelsFilter filter = new(null, null, null, null, MODEL_NAME, null);

    var existing = await client.Model.GetModels(projectId, 1, modelsFilter: filter);
    if (existing.items.Count != 0)
    {
      return existing.items[0];
    }

    CreateModelInput input = new(MODEL_NAME, null, projectId);
    return await client.Model.Create(input);
  }
}
