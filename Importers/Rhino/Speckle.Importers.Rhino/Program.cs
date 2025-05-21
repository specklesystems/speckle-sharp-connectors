﻿using Microsoft.Extensions.DependencyInjection;
using Rhino;
using Rhino.Runtime.InProcess;
using Speckle.Connectors.Common;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.Rhino.DependencyInjection;
using Speckle.Converters.Rhino;
using System.CommandLine;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Credentials;
using Speckle.Newtonsoft.Json;

namespace Speckle.Importers.Rhino;

public class RhinoImportResult
{
  [JsonProperty("success")]
  public bool Success { get; set; }

  [JsonProperty("commitId")]
  public string CommitId { get; set; }

  [JsonProperty("errorMessage")]
  public string ErrorMessage { get; set; }
}

public static class Program
{
  static Program()
  {
    Resolver.Initialize();
  }

  [STAThread]
  public static async Task<int> Main(string[] args)
  {
    RootCommand rootCommand = new();

    Argument<string> pathArg = new(name: "Source File Path", description: "Path to file to load and parse");
    Argument<string> resultsPathArg = new(name: "Results File Path", description: "Path to file to write results information (like version id)");
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

    rootCommand.SetHandler(
      async (filePath, resultsPath, projectId, modelId, serverUrl, token) =>
      {
        try
        {
          // Create file with account info here
          var accountsDir = SpecklePathProvider.AccountsFolderPath;
          if (!Directory.Exists(accountsDir))
          {
            Directory.CreateDirectory(accountsDir);
          }

          Account account = new()
          {
            token = token,
            isDefault = false,
            serverInfo = new Sdk.Api.GraphQL.Models.ServerInfo()
            {
              url = serverUrl
            },
            userInfo = new UserInfo()
            {
              name = "John Speckle",
              email = "john-speckle@example.org",
              id = "johnny-speckle"
            }
          };

          File.WriteAllText(Path.Combine(accountsDir, "user.json"), JsonConvert.SerializeObject(account));

          using (new RhinoCore([], WindowStyle.NoWindow))
          {
            using var doc = RhinoDoc.Open(filePath, out var _);
            var services = new ServiceCollection();
            // var path = Path.Combine(RhinoApp.GetExecutableDirectory().FullName, "Rhino.exe");
            services.Initialize(HostApplications.Rhino, HostAppVersion.v2026);
            services.AddRhino(false);
            services.AddRhinoConverters();
            // override default
            services.AddSingleton<IThreadContext>(new ImporterThreadContext());

            // but the Rhino connector has `.rhp` as it is extension.
            var container = services.BuildServiceProvider();
            var sender = ActivatorUtilities.CreateInstance<Sender>(container);
            var versionId = await sender.Send(projectId, modelId, new Uri(serverUrl));

            var result = versionId == null
            ? new RhinoImportResult()
            {
              Success = false,
              ErrorMessage = "Failed to create version!"
            }
            : new RhinoImportResult()
            {
              Success = true,
              CommitId = versionId
            };

            File.WriteAllText(resultsPath, JsonConvert.SerializeObject(result));
          }
        }
        catch (Exception ex)
        {
          Console.WriteLine(ex);
          var results = new RhinoImportResult()
          {
            Success = false,
            ErrorMessage = ex.Message,
          };
          File.WriteAllText(resultsPath, JsonConvert.SerializeObject(results));
          throw;
        }
      },
      pathArg,
      resultsPathArg,
      projectIdArg,
      modelIdArg,
      serverUrlArg,
      tokenArg
    );

    await rootCommand.InvokeAsync(args).ConfigureAwait(false);

    return 0;
  }
}

public class ImporterThreadContext : ThreadContext
{
  protected override Task<T> WorkerToMainAsync<T>(Func<Task<T>> action)
  {
    var t = Task.Factory.StartNew(action, default, TaskCreationOptions.AttachedToParent, TaskScheduler.Default);
    return t.Unwrap();
  }

  protected override Task<T> MainToWorkerAsync<T>(Func<Task<T>> action)
  {
    Task<Task<T>> f = Task.Factory.StartNew(
      action,
      default,
      TaskCreationOptions.AttachedToParent,
      TaskScheduler.Default
    );
    return f.Unwrap();
  }

  protected override Task<T> WorkerToMain<T>(Func<T> action)
  {
    var t = Task.Factory.StartNew(action, default, TaskCreationOptions.AttachedToParent, TaskScheduler.Default);
    return t;
  }

  protected override Task<T> MainToWorker<T>(Func<T> action)
  {
    Task<T> f = Task.Factory.StartNew(action, default, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    return f;
  }
}
