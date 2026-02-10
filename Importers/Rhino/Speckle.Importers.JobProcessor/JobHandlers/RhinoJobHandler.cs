using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Extensions;
using Speckle.Importers.JobProcessor.Blobs;
using Speckle.Importers.JobProcessor.Domain;
using Speckle.Newtonsoft.Json;
using Speckle.Sdk;
using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Inputs;
using Speckle.Sdk.Api.GraphQL.Models;
using Speckle.Sdk.Common;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Speckle.Importers.JobProcessor.JobHandlers;

internal sealed class RhinoJobHandler(
  ILogger<RhinoJobHandler> logger,
  ImportJobFileDownloader fileDownloader,
  ISpeckleApplication application
) : IJobHandler
{
  private readonly JsonSerializerSettings _settings =
    new() { TypeNameHandling = TypeNameHandling.All, MissingMemberHandling = MissingMemberHandling.Error, };

  public async Task<string> ProcessJob(
    FileimportJob job,
    IClient client,
    ModelIngestion ingestion,
    CancellationToken cancellationToken
  )
  {
    using var file = await fileDownloader.DownloadFile(job, client, cancellationToken);
    Project project = await client.Project.Get(job.Payload.ProjectId, cancellationToken);

    string fileType = file.FileInfo.Extension.TrimStart('.');
    Application handlerApplication = new($"Rhino .{fileType} File Import ", $"{fileType}-rhino-importer");

    ingestion = await client.Ingestion.StartProcessing(
      new ModelIngestionStartProcessingInput(
        ingestionId: ingestion.id,
        projectId: job.Payload.ProjectId,
        progressMessage: "Starting Up Importer",
        sourceData: new(
          handlerApplication.Slug,
          application.HostApplicationVersion,
          job.Payload.FileName,
          file.FileInfo.Length
        )
      ),
      cancellationToken
    );

    var importerArgs = new ImporterArgs
    {
      FilePath = file.FileInfo.FullName,
      ResultsPath = $"{file.FileInfo.DirectoryName}/results.json",
      Account = client.Account,
      Project = project,
      Ingestion = ingestion,
      JobId = job.Id,
      BlobId = job.Payload.BlobId,
      Attempt = job.Attempt,
      HostApplication = handlerApplication,
    };
    await RunSubProcess(importerArgs, cancellationToken);
    var response = await DeserializeResponse(importerArgs.ResultsPath, cancellationToken);

    if (response.RootObjectId is null)
    {
      string message = response.ErrorMessage ?? "Import job failed without a message";
      throw new SpeckleException(message);
    }

    return response.RootObjectId;
  }

  private async Task RunSubProcess(ImporterArgs args, CancellationToken cancellationToken)
  {
    using Process process = StartProcess(JsonSerializer.Serialize(args));

    using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    linked.Token.Register(_ => process.Kill(), null);

    await process.WaitForExitAsync(linked.Token);

    logger.LogInformation("Subprocess finished with {ExitCode}", process.ExitCode);
  }

  private static Process StartProcess(string serializedArgs)
  {
    List<string> argList = [serializedArgs];
    string path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location).NotNull();
    var processStart = new ProcessStartInfo()
    {
      FileName = $"{path}/Speckle.Importers.Rhino.exe",
      Environment = { },
      RedirectStandardError = true,
      RedirectStandardOutput = true,
      UseShellExecute = false,
    };
    processStart.ArgumentList.AddRange(argList);
    var process = new Process { StartInfo = processStart, EnableRaisingEvents = true, };
    // Capture output asynchronously
    process.OutputDataReceived += (_, e) =>
    {
      if (!string.IsNullOrEmpty(e.Data))
      {
        Console.WriteLine("[stdout] " + e.Data);
      }
    };

    process.ErrorDataReceived += (_, e) =>
    {
      if (!string.IsNullOrEmpty(e.Data))
      {
        Console.WriteLine("[stderr] " + e.Data);
      }
    };

    if (!process.Start())
    {
      throw new SpeckleException("Process did not start");
    }

    process.BeginOutputReadLine();
    process.BeginErrorReadLine();

    return process;
  }

  private async Task<ImporterResponse> DeserializeResponse(string path, CancellationToken cancellationToken)
  {
    try
    {
      string response = await File.ReadAllTextAsync(path, cancellationToken);

      return JsonConvert.DeserializeObject<ImporterResponse>(response, _settings);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      throw;
    }
    catch (Exception ex)
    {
      throw new SpeckleException("Importer left an invalid response", ex);
    }
  }
}
