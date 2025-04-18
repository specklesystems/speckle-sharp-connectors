#pragma warning disable CA1506
using System.Diagnostics;
using Ara3D.Utils;
//using JetBrains.Profiler.SelfApi;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Importers.Ifc;
using Speckle.Importers.Ifc.Ara3D.IfcParser;
using Speckle.Importers.Ifc.Converters;
using Speckle.Importers.Ifc.Tester;
using Speckle.Importers.Ifc.Types;
using Speckle.Sdk.Serialisation.V2;
using Speckle.Sdk.Serialisation.V2.Send;
using Speckle.Sdk.SQLite;

var serviceProvider = Import.GetServiceProvider();

//DotMemory.Init();
var filePath = new FilePath(
  //"C:\\Users\\adam\\Git\\speckle-server\\packages\\fileimport-service\\ifc-dotnet\\ifcs\\20210221PRIMARK.ifc"
  //"C:\\Users\\adam\\Git\\speckle-server\\packages\\fileimport-service\\ifc-dotnet\\ifcs\\231110ADT-FZK-Haus-2005-2006.ifc"
  //"C:\\Users\\adam\\Downloads\\T03PV06IMPMI01C.ifc"
  "C:\\Users\\adam\\Downloads\\20231128_HW_Bouwkosten.ifc"
);

var ifcFactory = serviceProvider.GetRequiredService<IIfcFactory>();
var stopwatch = Stopwatch.StartNew();

Console.WriteLine($"Opening with WebIFC: {filePath}");
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

var factory = serviceProvider.GetRequiredService<ISerializeProcessFactory>();
var cache = $"C:\\Users\\adam\\Git\\temp\\{Guid.NewGuid()}.db";
using var sqlite = new SqLiteJsonCacheManager($"Data Source={cache};", 2);
await using var process2 = factory.CreateSerializeProcess(
  sqlite,
  new DummyServerObjectManager(),
  new Progress(true),
  default,
  new SerializeProcessOptions(SkipServer: true)
);
Console.WriteLine($"Caching to Speckle: {cache}");

/*var config = new DotMemory.Config();
config.OpenDotMemory();
config.SaveToDir("C:\\Users\\adam\\dotTraceSnapshots");
DotMemory.Attach(config);
DotMemory.GetSnapshot("Before");*/
var (rootId, _) = await process2.Serialize(b).ConfigureAwait(false);
Console.WriteLine(rootId);
ms2 = stopwatch.ElapsedMilliseconds;
Console.WriteLine($"Converted to JSON: {ms2 - ms} ms");
//DotMemory.GetSnapshot("After");
//DotMemory.Detach();
#pragma warning restore CA1506
