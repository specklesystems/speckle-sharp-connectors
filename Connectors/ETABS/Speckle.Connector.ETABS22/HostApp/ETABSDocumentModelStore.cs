using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Utils;

namespace Speckle.Connector.ETABS22.HostApp;

public class ETABSDocumentModelStore : DocumentModelStore
{
  public ETABSDocumentModelStore(IJsonSerializer jsonSerializerSettings)
    : base(jsonSerializerSettings, true) { }

  public override void WriteToFile() { }

  public override void ReadFromFile() { }
}
