using Speckle.Connectors.DUI.Models;
using Speckle.Newtonsoft.Json;

namespace Speckle.Connector.ETABS22.HostApp;

public class ETABSDocumentModelStore : DocumentModelStore
{
  public ETABSDocumentModelStore(JsonSerializerSettings jsonSerializerSettings)
    : base(jsonSerializerSettings, true) { }

  public override void WriteToFile() { }

  public override void ReadFromFile() { }
}
