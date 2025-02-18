using Ara3D.Utils;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Importers.Ifc;
using Speckle.Importers.Ifc.Ara3D.IfcParser;
using Speckle.Importers.Ifc.Converters;
using Speckle.Importers.Ifc.Tester2;
using Speckle.Importers.Ifc.Types;
using Speckle.Objects.Geometry;
using Speckle.Sdk.Host;
using Speckle.Sdk.Models;

// Settings
var filePath = new FilePath(@"C:\Users\Jedd\Desktop\231110AC11-Institute-Var-2-IFC.ifc");
const string PROJECT_ID = "f3a42bdf24";

// Setup
TypeLoader.Initialize(typeof(Base).Assembly, typeof(Point).Assembly);

var serviceCollection = new ServiceCollection();
serviceCollection.AddIFCImporter();
serviceCollection.AddSingleton<Sender>();
var serviceProvider = serviceCollection.BuildServiceProvider();

// Convert IFC to Speckle Objects
var ifcFactory = serviceProvider.GetRequiredService<IIfcFactory>();
var model = ifcFactory.Open(filePath);
var graph = IfcGraph.Load(new FilePath(filePath));
var converter = serviceProvider.GetRequiredService<IGraphConverter>();
var b = converter.Convert(model, graph);

//Send Speckle Objects to server
var sender = serviceProvider.GetRequiredService<Sender>();
await sender.Send(b, PROJECT_ID);
