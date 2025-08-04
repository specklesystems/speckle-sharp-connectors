using Ara3D.Utils;
using Speckle.Sdk.Api;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Models.Extensions;

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
  // private readonly ICollection<FilePath> _filePath = [new(@"C:\Users\Jedd\Desktop\GRAPHISOFT_Archicad_Sample_Project-S-Office_v1.0_AC25.ifc")]
  private readonly IEnumerable<string> _filePaths = Directory.EnumerateFiles(
    @"C:\Users\Jedd\Desktop\big files",
    "*.ifc"
  );

  private readonly Uri _serverUrl = new("https://app.speckle.systems");
  private const string PROJECT_ID = "f3a42bdf24";

  public async Task Run(CancellationToken cancellationToken = default)
  {
    var account = accountManager.GetAccounts(_serverUrl).First();
    using var speckleClient = clientFactory.Create(account);

    foreach (var path in _filePaths)
    {
      try
      {
        await ImportFile(speckleClient, path, cancellationToken);
      }
#pragma warning disable CA1031
      catch (Exception ex)
#pragma warning restore CA1031
      {
        Console.WriteLine(ex.ToFormattedString());
      }
    }
  }

  private async Task ImportFile(IClient speckleClient, FilePath filePath, CancellationToken cancellationToken)
  {
    string modelName = filePath.GetFileName();
    var existing = await speckleClient.Project.GetWithModels(
      PROJECT_ID,
      1,
      modelsFilter: new(search: modelName),
      cancellationToken: cancellationToken
    );
    string? existingModel = existing.models.items.Count >= 1 ? existing.models.items.First().id : null;

    // Convert IFC to Speckle Objects

    ImporterArgs args =
      new()
      {
        ServerUrl = _serverUrl,
        FilePath = filePath.ToString(),
        ProjectId = PROJECT_ID,
        ModelId = existingModel,
        ModelName = filePath.GetFileName(),
        VersionMessage = "",
        Token = speckleClient.Account.token
      };
    var version = await importer.ImportIfc(args, null, cancellationToken);
    Console.WriteLine($"File was successfully sent {version.id}");
  }
}
