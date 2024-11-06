using Speckle.Connectors.DUI.Models;
using Speckle.Newtonsoft.Json;

namespace Speckle.Connector.Navisworks.HostApp;

public class NavisworksDocumentStore : DocumentModelStore
{
  public NavisworksDocumentStore(JsonSerializerSettings serializerOptions)
    : base(serializerOptions, true) { }

  public override void WriteToFile() { }

  public override void ReadFromFile() { }
}
