using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rhino;
using Rhino.Runtime.InProcess;
using Serilog;
using Serilog.Formatting.Compact;
using Speckle.Connectors.Common;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.Rhino.DependencyInjection;
using Speckle.Converters.Rhino;
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

#pragma warning disable CA1506
  private static async Task Handle(
    string filePath,
    string resultsPath,
    string projectId,
    string modelId,
#pragma warning restore CA1506
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

    using (new RhinoCore([], WindowStyle.NoWindow))
    {
      //doc is often null so dispose the active doc too
      using var doc = RhinoDoc.Open(filePath, out _);
      using var __ = RhinoDoc.ActiveDoc;
      var services = new ServiceCollection();
      services.Initialize(HostApplications.RhinoImporter, HostAppVersion.v8);
      services.AddRhino(false);
      services.AddRhinoConverters();
      // override default
      services.AddSingleton<IThreadContext>(new ImporterThreadContext());
      services.AddTransient<Progress>();
      Log.Logger = new LoggerConfiguration()
        .Enrich.FromLogContext()
        .WriteTo.Console(new RenderedCompactJsonFormatter())
        .CreateLogger();
      services.AddLogging(loggingBuilder =>
      {
        loggingBuilder.ClearProviders();
        loggingBuilder.AddSerilog(dispose: true);
      });

      // but the Rhino connector has `.rhp` as it is extension.
      var container = services.BuildServiceProvider();
      try
      {
        var sender = ActivatorUtilities.CreateInstance<Sender>(container);
        var version = await sender.Send(projectId, modelId, new Uri(serverUrl), token);

        var result =
          version == null
            ? new RhinoImportResult() { Success = false, ErrorMessage = "Failed to create version!" }
            : new RhinoImportResult() { Success = true, CommitId = version.id };

        File.WriteAllText(resultsPath, JsonConvert.SerializeObject(result));
      }
      catch (Exception ex)
      {
        container.GetRequiredService<ILogger<Sender>>().LogError(ex, "Fatal error for import");
        var results = new RhinoImportResult() { Success = false, ErrorMessage = ex.Message, };
        File.WriteAllText(resultsPath, JsonConvert.SerializeObject(results));
        throw;
      }
    }
  }
}
