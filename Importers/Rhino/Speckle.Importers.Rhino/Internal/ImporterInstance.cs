using System.Diagnostics.Contracts;
using Microsoft.Extensions.Logging;
using Rhino;
using Rhino.Runtime.InProcess;
using Speckle.Connectors.Common.Extensions;
using Speckle.Connectors.Logging;
using Speckle.Importers.Rhino.Internal.FileTypeConfig;
using Version = Speckle.Sdk.Api.GraphQL.Models.Version;

namespace Speckle.Importers.Rhino.Internal;

internal sealed class ImporterInstance(ImporterArgs args, Sender sender, ILogger<ImporterInstance> logger) : IDisposable
{
  private readonly RhinoCore _rhinoInstance = new(["/netcore-8"], WindowStyle.NoWindow);

  private readonly RhinoDoc _rhinoDoc = OpenDocument(args, logger);

  private readonly IReadOnlyList<IDisposable> _scopes =
  [
    ActivityScope.SetTag("jobId", args.JobId),
    ActivityScope.SetTag("job.attempt", args.Attempt.ToString()),
    // ActivityScope.SetTag("jobType", args.JobType),
    ActivityScope.SetTag("serverUrl", args.Account.serverInfo.url),
    ActivityScope.SetTag("projectId", args.Project.id),
    ActivityScope.SetTag("modelId", args.ModelId),
    ActivityScope.SetTag("blobId", args.BlobId),
    ActivityScope.SetTag("fileType", Path.GetExtension(args.FilePath).TrimStart('.')),
    UserActivityScope.AddUserScope(args.Account),
  ];

  public async Task<Version> RunRhinoImport(ImporterArgs args, CancellationToken cancellationToken)
  {
    try
    {
      RhinoDoc.ActiveDoc = _rhinoDoc;

      var version = await sender
        .Send(args.Project, args.ModelId, args.Account, cancellationToken)
        .ConfigureAwait(false);
      return version;
    }
    finally
    {
      RhinoDoc.ActiveDoc = null;
    }
  }

  private static RhinoDoc OpenDocument(ImporterArgs args, ILogger logger)
  {
    using var config = GetConfig(Path.GetExtension(args.FilePath));
    logger.LogInformation("Opening file {FilePath}", args.FilePath);
    return config.OpenInHeadlessDocument(args.FilePath);
  }

  [Pure]
  private static IFileTypeConfig GetConfig(string extension) =>
    extension.ToLowerInvariant() switch
    {
      ".skp" => new SketchupConfig(),
      ".obj" => new ObjConfig(),
      ".3dm" => new Rhino3dmConfig(),
      ".fbx" => new FbxConfig(),
      _ => new DefaultConfig(),
    };

  public void Dispose()
  {
    //There is a bug in Rhino.Inside (>=8.x)
    //that will cause an unmanaged crash when disposing 3dm documents
    // https://discourse.mcneel.com/t/rhino-inside-fatal-app-crashes-when-disposing-headless-documents/208673
    _rhinoDoc?.Dispose();
    _rhinoInstance.Dispose();
    foreach (var scope in _scopes)
    {
      scope.Dispose();
    }
  }
}
