using System.Diagnostics;
using Ara3D.Utils;
using Speckle.Importers.Ifc.Ara3D.IfcParser;
using Speckle.Importers.Ifc.Converters;
using Speckle.Importers.Ifc.Types;
using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Inputs;
using Speckle.Sdk.Api.GraphQL.Models;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Serialisation.V2;
using Speckle.Sdk.Serialisation.V2.Send;
using Speckle.Sdk.Transports;
using Version = Speckle.Sdk.Api.GraphQL.Models.Version;

namespace Speckle.Importers.Ifc;

public sealed class ImporterArgs
{
  public required Uri ServerUrl { get; init; }
  public required string FilePath { get; init; }
  public required string ProjectId { get; init; }
  public required string? ModelId { get; init; }
  public required string ModelName { get; init; }
  public required string VersionMessage { get; init; }
  public required string Token { get; init; }
}

public sealed class Importer(
  IIfcFactory ifcFactory,
  IClientFactory clientFactory,
  IGraphConverter converter,
  ISerializeProcessFactory serializeProcessFactory
)
{
  public async Task<Version> ImportIfc(
    ImporterArgs args,
    IProgress<ProgressArgs>? progress,
    CancellationToken cancellationToken
  )
  {
    var stopwatch = Stopwatch.StartNew();

    var ifcModel = ifcFactory.Open(args.FilePath);
    var ms = stopwatch.ElapsedMilliseconds;
    Console.WriteLine($"Opened with WebIFC: {ms} ms");

    var graph = IfcGraph.Load(new FilePath(args.FilePath));
    var ms2 = stopwatch.ElapsedMilliseconds;
    Console.WriteLine($"Loaded with StepParser: {ms2 - ms} ms");

    var b = converter.Convert(ifcModel, graph);
    ms = ms2;
    ms2 = stopwatch.ElapsedMilliseconds;
    Console.WriteLine($"Converted to Speckle Bases: {ms2 - ms} ms");

    var process = serializeProcessFactory.CreateSerializeProcess(
      args.ServerUrl,
      args.ProjectId,
      args.Token,
      progress,
      cancellationToken,
      new SerializeProcessOptions(true, true, false, progress is null)
    );
    var (rootId, _) = await process.Serialize(b).ConfigureAwait(false);
    Account account =
      new()
      {
        token = args.Token,
        serverInfo = new ServerInfo { url = args.ServerUrl.ToString() },
      };
    ms = ms2;
    ms2 = stopwatch.ElapsedMilliseconds;
    Console.WriteLine($"Uploaded to Speckle: {ms2 - ms} ms. Root id: {rootId}");

    // 8 - Create the version (commit)
    using var apiClient = clientFactory.Create(account);
    var modelId = args.ModelId;
    if (string.IsNullOrEmpty(modelId))
    {
      // Project level import, currently we're expecting the parsers to create the branch
      // Quite smelly imo...
      var input = new CreateModelInput(args.ModelName, null, args.ProjectId);
      var model = await apiClient.Model.Create(input, cancellationToken);
      modelId = model.id;
    }

    var speckleVersion = await apiClient.Version.Create(
      new CreateVersionInput(rootId, modelId, args.ProjectId, message: args.VersionMessage, sourceApplication: "IFC"),
      cancellationToken
    );
    ms = ms2;
    ms2 = stopwatch.ElapsedMilliseconds;
    Console.WriteLine($"Committed to Speckle: {ms2 - ms} ms");
    return speckleVersion;
  }
}
