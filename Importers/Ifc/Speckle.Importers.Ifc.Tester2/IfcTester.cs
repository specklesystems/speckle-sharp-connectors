using Ara3D.Utils;
using Speckle.Sdk.Api;
using Speckle.Sdk.Credentials;

namespace Speckle.Importers.Ifc.Tester2;

/// <summary>
/// This is a test file for testing the IFC importer locally
/// Except for the logic in the preview service, this is pretty much exactly the same as what is running when
/// you upload an ifc file.
/// </summary>
/// <param name="clientFactory"></param>
/// <param name="importer"></param>
/// <param name="accountManager"></param>
public sealed class IfcTester(IClientFactory clientFactory, Importer importer, IAccountManager accountManager)
{
  // Settings, Change these to suit!
  private readonly FilePath _filePath =
    //new(@"C:\Users\Jedd\Desktop\GRAPHISOFT_Archicad_Sample_Project-S-Office_v1.0_AC25.ifc");
    new(@"C:\Users\Jedd\Desktop\EST-BRE-AF-3D-BT1-30-SD-00001-A-P.ifc");
  private readonly Uri _serverUrl = new("https://app.speckle.systems");
  private const string PROJECT_ID = "f3a42bdf24";

  public async Task Run()
  {
    var account = accountManager.GetAccounts(_serverUrl).First();
    using var speckleClient = clientFactory.Create(account);
    string modelName = _filePath.GetFileName();
    var existing = await speckleClient.Project.GetWithModels(PROJECT_ID, 1, modelsFilter: new(search: modelName));
    string? existingModel = existing.models.items.Count >= 1 ? existing.models.items.First().id : null;

    // Convert IFC to Speckle Objects

    ImporterArgs args =
      new()
      {
        ServerUrl = _serverUrl,
        FilePath = _filePath.ToString(),
        ProjectId = PROJECT_ID,
        ModelId = existingModel,
        ModelName = _filePath.GetFileName(),
        VersionMessage = "",
        Token = account.token
      };
    var version = await importer.ImportIfc(args, null, default);
    Console.WriteLine($"File was successfully sent {version.id}");
  }
}
