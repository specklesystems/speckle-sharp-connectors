using System.Diagnostics;
using System.Reflection;
using Ara3D.IfcParser;
using Ara3D.Utils;
using Microsoft.Extensions.DependencyInjection;
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
using Speckle.WebIfc.Importer.Converters;
using Speckle.WebIfc.Importer.Ifc;

namespace Speckle.WebIfc.Importer;

public static class Import
{
  public static async Task<string> Ifc(
    string url,
    string filePath,
    string streamId,
    string modelId,
    string commitMessage,
    string token,
    IProgress<ProgressArgs>? progress = null
  )
  {
    var serviceProvider = GetServiceProvider();
    return await Ifc(
      serviceProvider,
      url,
      filePath,
      streamId,
      modelId,
      commitMessage,
      token,
      progress
    );
  }

  public static ServiceProvider GetServiceProvider()
  {
    TypeLoader.Initialize(typeof(Base).Assembly, typeof(Point).Assembly);
    var serviceCollection = new ServiceCollection();
    serviceCollection.AddSpeckleSdk(HostApplications.Other, HostAppVersion.v2024, "IFC-Importer");
    serviceCollection.AddSpeckleWebIfc();
    serviceCollection.AddMatchingInterfacesAsTransient(Assembly.GetExecutingAssembly());
    return serviceCollection.BuildServiceProvider();
  }

  public static async Task<string> Ifc(
    IServiceProvider serviceProvider,
    string url,
    string filePath,
    string streamId,
    string modelId,
    string commitMessage,
    string token,
    IProgress<ProgressArgs>? progress = null
  )
  {
    var ifcFactory = serviceProvider.GetRequiredService<IIfcFactory>();
    var clientFactory = serviceProvider.GetRequiredService<IClientFactory>();
    var baseUri = new Uri(url);
    var stopwatch = Stopwatch.StartNew();

    var model = ifcFactory.Open(filePath);
    var ms = stopwatch.ElapsedMilliseconds;
    Console.WriteLine($"Opened with WebIFC: {ms} ms");

    var graph = IfcGraph.Load(new FilePath(filePath));
    var ms2 = stopwatch.ElapsedMilliseconds;
    Console.WriteLine($"Loaded with StepParser: {ms2 - ms} ms");

    var converter = serviceProvider.GetRequiredService<IGraphConverter>();
    var b = converter.Convert(model, graph);
    ms = ms2;
    ms2 = stopwatch.ElapsedMilliseconds;
    Console.WriteLine($"Converted to Speckle Bases: {ms2 - ms} ms");

    var serializeProcessFactory = serviceProvider.GetRequiredService<ISerializeProcessFactory>();
    var process = serializeProcessFactory.CreateSerializeProcess(
      baseUri,
      streamId,
      token,
      progress,
      new SerializeProcessOptions(true, true, true, false)
    );
    var (rootId, _) = await process.Serialize(b, default).ConfigureAwait(false);
    Account account = new()
    {
      token = token,
      serverInfo = new ServerInfo { url = baseUri.ToString() },
    };
    ms = ms2;
    ms2 = stopwatch.ElapsedMilliseconds;
    Console.WriteLine($"Uploaded to Speckle: {ms2 - ms} ms");

    // 8 - Create the version (commit)
    using var apiClient = clientFactory.Create(account);
    var commitId = await apiClient.Version.Create(
      new CreateVersionInput(rootId, modelId, streamId, message: commitMessage)
    );
    ms = ms2;
    ms2 = stopwatch.ElapsedMilliseconds;
    Console.WriteLine($"Committed to Speckle: {ms2 - ms} ms");
    return commitId;
  }
}
