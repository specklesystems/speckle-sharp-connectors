using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Speckle.Newtonsoft.Json;
using Speckle.Sdk.Logging;

namespace Speckle.Importers.Rhino;

public static class Program
{
  static Program()
  {
    Resolver.Initialize();
  }

  [STAThread]
  public static async Task<int> Main(string[] args)
  {
    try
    {
      RootCommand rootCommand = new();

      Argument<string> pathArg = new(name: "Source File Path", description: "Path to file to load and parse");
      Argument<string> resultsPathArg =
        new(name: "Results File Path", description: "Path to file to write results information (like version id)");
      Argument<string> projectIdArg = new(name: "Project Id", description: "The project id to publish to");
      Argument<string> modelIdArg = new(name: "Model Id", description: "The model id to publish to");
      Argument<string> serverUrlArg = new(name: "Server Url", description: "The url of the server to publish to.");
      Argument<string> tokenArg = new(name: "Token", description: "The speckle token to use for publish");

      rootCommand.AddArgument(pathArg);
      rootCommand.AddArgument(resultsPathArg);
      rootCommand.AddArgument(projectIdArg);
      rootCommand.AddArgument(modelIdArg);
      rootCommand.AddArgument(serverUrlArg);
      rootCommand.AddArgument(tokenArg);

      rootCommand.SetHandler(Handle, pathArg, resultsPathArg, projectIdArg, modelIdArg, serverUrlArg, tokenArg);

      await rootCommand.InvokeAsync(args).ConfigureAwait(false);

      return 0;
    }
#pragma warning disable CA1031
    catch (Exception e)
#pragma warning restore CA1031
    {
      Console.WriteLine(e);
      return -1;
    }
  }

  private static async Task Handle(
    string filePath,
    string resultsPath,
    string projectId,
    string modelId,
    string serverUrl,
    string token
  )
  {
    // Create file with account info here
    var accountsDir = SpecklePathProvider.AccountsFolderPath;
    if (!Directory.Exists(accountsDir))
    {
      Directory.CreateDirectory(accountsDir);
    }

    var services = new ServiceCollection();
    services.AddRhinoImporter();
    var container = services.BuildServiceProvider();

    var importer = container.GetRequiredService<Importer>();
    var logger = container.GetRequiredService<ILogger>();

    try
    {
      var result = await importer.Import(filePath, projectId, modelId, new(serverUrl), token);
      File.WriteAllText(resultsPath, JsonConvert.SerializeObject(result));
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Fatal error for import");
      var results = new { Success = false, ErrorMessage = ex.Message, };
      File.WriteAllText(resultsPath, JsonConvert.SerializeObject(results));
      throw;
    }
  }
}
