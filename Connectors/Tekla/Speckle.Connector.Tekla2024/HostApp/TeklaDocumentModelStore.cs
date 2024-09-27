using Speckle.Connectors.DUI.Models;
using Speckle.Newtonsoft.Json;

namespace Speckle.Connector.Tekla2024.HostApp;

public class TeklaDocumentModelStore : DocumentModelStore
{
  public TeklaDocumentModelStore(
    JsonSerializerSettings jsonSerializerSettings
  // ITopLevelExceptionHandler topLevelExceptionHandler
  )
    : base(jsonSerializerSettings, true) { }

  public override void WriteToFile() { }

  public override void ReadFromFile() { }
}
