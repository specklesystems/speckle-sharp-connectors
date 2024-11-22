using Speckle.Connectors.DUI.Models;
using Speckle.Newtonsoft.Json;

namespace Speckle.Connector.Navisworks.HostApp;

public class NavisworksDocumentStore(JsonSerializerSettings serializerOptions)
  : DocumentModelStore(serializerOptions, true)
{
  public override void WriteToFile() { }

  public override void ReadFromFile() { }
}
