using System.Diagnostics.Contracts;
using Rhino;
using Rhino.Runtime.InProcess;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Extensions;
using Speckle.Connectors.Logging;
using Speckle.Importers.Rhino.Internal.FileTypeConfig;
using Speckle.Sdk.Api;
using Speckle.Sdk.Logging;

namespace Speckle.Importers.Rhino.Internal;

internal sealed class ImporterInstance : IDisposable
{
  private readonly RhinoCore _rhinoInstance = new(["/netcore-8"], WindowStyle.NoWindow);

  private readonly RhinoDoc _rhinoDoc;

  private readonly IReadOnlyList<IDisposable> _scopes;

  private readonly ImporterArgs _args;
  private readonly Sender _sender;
  private readonly IClient _speckleClient;
  private readonly ISdkActivityFactory _activityFactory;

  public ImporterInstance(ImporterArgs args, Sender sender, IClient speckleClient, ISdkActivityFactory activityFactory)
  {
    _args = args;
    _sender = sender;
    _speckleClient = speckleClient;
    _activityFactory = activityFactory;
    _rhinoDoc = OpenDocument();
    _scopes =
    [
      ActivityScope.SetTag("jobId", args.JobId),
      ActivityScope.SetTag("job.attempt", args.Attempt.ToString()),
      // ActivityScope.SetTag("jobType", args.JobType),
      ActivityScope.SetTag("serverUrl", new Uri(args.Account.serverInfo.url).ToString()),
      ActivityScope.SetTag("projectId", args.Project.id),
      ActivityScope.SetTag("modelIngestion.id", args.Ingestion.id),
      ActivityScope.SetTag("modelId", args.Ingestion.modelId),
      ActivityScope.SetTag("blobId", args.BlobId),
      ActivityScope.SetTag("fileType", Path.GetExtension(args.FilePath).TrimStart('.')),
      UserActivityScope.AddUserScope(args.Account),
    ];
  }

  public async Task<RootObjectBuilderResult> RunRhinoImport(CancellationToken cancellationToken)
  {
    using var activity = _activityFactory.Start();
    try
    {
      RhinoDoc.ActiveDoc = _rhinoDoc;
      var results = await _sender
        .Send(_args.Project, _args.Ingestion, _speckleClient, cancellationToken)
        .ConfigureAwait(false);
      activity?.SetStatus(SdkActivityStatusCode.Ok);
      return results;
    }
    catch (Exception ex)
    {
      activity?.RecordException(ex);
      activity?.SetStatus(SdkActivityStatusCode.Error);
      throw;
    }
    finally
    {
      RhinoDoc.ActiveDoc = null;
    }
  }

  private RhinoDoc OpenDocument()
  {
    using var activity = _activityFactory.Start();
    try
    {
      using var config = GetConfig(Path.GetExtension(_args.FilePath));
      RhinoDoc openedDoc = config.OpenInHeadlessDocument(_args.FilePath);

      activity?.SetStatus(SdkActivityStatusCode.Ok);
      return openedDoc;
    }
    catch (Exception ex)
    {
      activity?.RecordException(ex);
      activity?.SetStatus(SdkActivityStatusCode.Error);
      throw;
    }
  }

  [Pure]
  private static IFileTypeConfig GetConfig(string extension) =>
    extension.ToLowerInvariant() switch
    {
      ".skp" => new SketchupConfig(),
      ".obj" => new ObjConfig(),
      ".dgn" => new DgnConfig(),
      ".fbx" => new FbxConfig(),
      ".dwg" => new DwgConfig(),
      ".dxf" => new DwgConfig(),
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
    _speckleClient.Dispose();
  }
}
