using Ara3D.Utils;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Importers.Ifc;
using Speckle.Objects.Geometry;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Host;
using Speckle.Sdk.Models;
using Version = Speckle.Sdk.Api.GraphQL.Models.Version;

/*
 * This is a test file for testing the IFC importer locally
 * Except for the logic in the preview service, this is pretty much exactly the same as what is running when
 * you upload an ifc file.
 */

// Settings, Change these to suit!
var filePath = new FilePath(@"C:\Users\Jedd\Desktop\KLY-ZHQ-B-CPL1_CPL4-0-ELV-SD-210.ifc");
Uri serverUrl = new("https://app.speckle.systems");
const string PROJECT_ID = "f3a42bdf24";

// Setup
TypeLoader.Initialize(typeof(Base).Assembly, typeof(Point).Assembly);
var serviceCollection = new ServiceCollection();
serviceCollection.AddIFCImporter();
var serviceProvider = serviceCollection.BuildServiceProvider();
var accounts = serviceProvider.GetRequiredService<IAccountManager>();

// Convert IFC to Speckle Objects
Version version = await Import.Ifc(
  serviceProvider,
  serverUrl,
  filePath,
  PROJECT_ID,
  default,
  "Ifc-Tester2",
  "",
  accounts.GetAccounts(serverUrl).First().token
);

Console.WriteLine($"File was successfully sent {version.id}");
