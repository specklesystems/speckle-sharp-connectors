using System.Diagnostics;
using System.Reflection;
using Ara3D.Utils;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Importers.Ifc.Ara3D.IfcParser;
using Speckle.Importers.Ifc.Converters;
using Speckle.Importers.Ifc.Types;
using Speckle.Objects.Geometry;
using Speckle.Sdk;
using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Inputs;
using Speckle.Sdk.Api.GraphQL.Models;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Host;
using Speckle.Sdk.Models;
using Speckle.Sdk.Serialisation.V2;
using Speckle.Sdk.Serialisation.V2.Send;
using Speckle.Sdk.Transports;
using Version = Speckle.Sdk.Api.GraphQL.Models.Version;

namespace Speckle.Importers.Ifc;

public static class Import
{
  public static async Task<Version> Ifc(
    Uri url,
    string filePath,
    string streamId,
    string? modelId,
    string modelName,
    string versionMessage,
    string token,
    IProgress<ProgressArgs>? progress = null
  )
  {
    var serviceProvider = GetServiceProvider();
    return await Ifc(serviceProvider, url, filePath, streamId, modelId, modelName, versionMessage, token, progress);
  }

  public static ServiceProvider GetServiceProvider()
  {
    TypeLoader.Initialize(typeof(Base).Assembly, typeof(Point).Assembly);
    var serviceCollection = new ServiceCollection();
    serviceCollection.AddIFCImporter();
    return serviceCollection.BuildServiceProvider();
  }

  public static void AddIFCImporter(this ServiceCollection serviceCollection)
  {
    serviceCollection.AddSpeckleSdk(new("IFC", "ifc"), HostAppVersion.v2024, "IFC-Importer");
    serviceCollection.AddSpeckleWebIfc();
    serviceCollection.AddMatchingInterfacesAsTransient(Assembly.GetExecutingAssembly());
  }

  public static async Task<Version> Ifc(
    IServiceProvider serviceProvider,
    Uri url,
    string filePath,
    string projectId,
    string? modelId,
    string modelName,
    string versionMessage,
    string token,
    IProgress<ProgressArgs>? progress = null
  )
  {
    var ifcFactory = serviceProvider.GetRequiredService<IIfcFactory>();
    var clientFactory = serviceProvider.GetRequiredService<IClientFactory>();
    var stopwatch = Stopwatch.StartNew();

    var ifcModel = ifcFactory.Open(filePath);
    var ms = stopwatch.ElapsedMilliseconds;
    Console.WriteLine($"Opened with WebIFC: {ms} ms");

    var graph = IfcGraph.Load(new FilePath(filePath));
    var ms2 = stopwatch.ElapsedMilliseconds;
    Console.WriteLine($"Loaded with StepParser: {ms2 - ms} ms");

    var converter = serviceProvider.GetRequiredService<IGraphConverter>();
    var b = converter.Convert(ifcModel, graph);
    ms = ms2;
    ms2 = stopwatch.ElapsedMilliseconds;
    Console.WriteLine($"Converted to Speckle Bases: {ms2 - ms} ms");

    var serializeProcessFactory = serviceProvider.GetRequiredService<ISerializeProcessFactory>();
    var process = serializeProcessFactory.CreateSerializeProcess(
      url,
      projectId,
      token,
      progress,
      default,
      new SerializeProcessOptions(true, true, false, progress is null)
    );
    var (rootId, _) = await process.Serialize(b).ConfigureAwait(false);
    Account account =
      new()
      {
        token = token,
        serverInfo = new ServerInfo { url = url.ToString() },
      };
    ms = ms2;
    ms2 = stopwatch.ElapsedMilliseconds;
    Console.WriteLine($"Uploaded to Speckle: {ms2 - ms} ms. Root id: {rootId}");

    // 8 - Create the version (commit)
    using var apiClient = clientFactory.Create(account);

    if (string.IsNullOrEmpty(modelId))
    {
      // Project level import, currently we're expecting the parsers to create the branch
      // Quite smelly imo...
      var input = new CreateModelInput(modelName, null, projectId);
      var model = await apiClient.Model.Create(input);
      modelId = model.id;
    }

    var speckleVersion = await apiClient.Version.Create(
      new CreateVersionInput(rootId, modelId, projectId, message: versionMessage, sourceApplication: "IFC")
    );
    ms = ms2;
    ms2 = stopwatch.ElapsedMilliseconds;
    Console.WriteLine($"Committed to Speckle: {ms2 - ms} ms");
    return speckleVersion;
  }
}
