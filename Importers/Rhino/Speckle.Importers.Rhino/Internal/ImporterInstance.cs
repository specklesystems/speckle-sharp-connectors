using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Rhino;
using Rhino.Runtime.InProcess;
using Speckle.Connectors.Common.Extensions;
using Speckle.Connectors.Logging;
using Speckle.Importers.Rhino.Internal.FileTypeConfig;
using Version = Speckle.Sdk.Api.GraphQL.Models.Version;

namespace Speckle.Importers.Rhino.Internal;

internal sealed class ImporterInstance(Sender sender, ILogger<ImporterInstance> logger) : IDisposable
{
  private readonly ILogger _logger = logger;
  private readonly RhinoCore _rhinoInstance = new(["/netcore-8"], WindowStyle.NoWindow);
  private static readonly JsonSerializerOptions s_serializerOptions =
    new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, };

  private RhinoDoc? _rhinoDoc;

  public async Task Run(string[] args, CancellationToken cancellationToken)
  {
    var a = JsonSerializer.Deserialize<ImporterArgs>(args[0], s_serializerOptions);
    using var scopeJobId = ActivityScope.SetTag("jobId", a.JobId);
    // using var scopeJobType = ActivityScope.SetTag("jobType", a.JobType);
    using var scopeAttempt = ActivityScope.SetTag("job.attempt", a.Attempt.ToString());
    using var scopeServerUrl = ActivityScope.SetTag("serverUrl", a.Account.serverInfo.url);
    using var scopeProjectId = ActivityScope.SetTag("projectId", a.ProjectId);
    using var scopeModelId = ActivityScope.SetTag("modelId", a.ModelId);
    using var scopeBlobId = ActivityScope.SetTag("blobId", a.BlobId);
    UserActivityScope.AddUserScope(a.Account);

    var result = await TryImport(a, cancellationToken);
    var serializedResult = JsonSerializer.Serialize(result, s_serializerOptions);
    File.WriteAllLines(a.ResultsPath, [serializedResult]);
  }

  [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "IPC")]
  private async Task<ImporterResponse> TryImport(ImporterArgs args, CancellationToken cancellationToken)
  {
    try
    {
      var version = await RunRhinoImport(args, cancellationToken);
      return new ImporterResponse { Version = version, ErrorMessage = null };
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Import attempt failed with exception");
      return new ImporterResponse { ErrorMessage = ex.Message, Version = null };
    }
  }

  private async Task<Version> RunRhinoImport(ImporterArgs args, CancellationToken cancellationToken)
  {
    try
    {
      using var config = GetConfig(Path.GetExtension(args.FilePath));
      _rhinoDoc = config.OpenInHeadlessDocument(args.FilePath);
      RhinoDoc.ActiveDoc = _rhinoDoc;

      var version = await sender.Send(args.ProjectId, args.ModelId, args.Account, cancellationToken);
      return version;
    }
    finally
    {
      RhinoDoc.ActiveDoc = null;
    }
  }

  private static IFileTypeConfig GetConfig(string extension) =>
    extension.ToLowerInvariant() switch
    {
      ".skp" => new SketchupConfig(),
      ".obj" => new ObjConfig(),
      ".3dm" => new Rhino3dmConfig(),
      _ => new DefaultConfig(),
    };

  public void Dispose()
  {
    //There is a bug in Rhino.Inside (>=8.x)
    //that will cause an unmanaged crash when disposing 3dm documents
    // https://discourse.mcneel.com/t/rhino-inside-fatal-app-crashes-when-disposing-headless-documents/208673
    _rhinoDoc?.Dispose();
    _rhinoInstance.Dispose();
  }
}
