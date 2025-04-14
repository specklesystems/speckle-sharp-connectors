using Microsoft.Extensions.DependencyInjection;
using Speckle.Importers.Ifc;
using Speckle.Importers.Ifc.Tester2;

// This is all DI setup, Look in IfcTester.cs for the real goodies
var serviceCollection = new ServiceCollection();
serviceCollection.AddIFCImporter();
serviceCollection.AddSingleton<IfcTester>();
var serviceProvider = serviceCollection.BuildServiceProvider();

var tester = serviceProvider.GetRequiredService<IfcTester>();
await tester.Run();
