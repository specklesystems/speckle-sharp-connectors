using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Utils;

namespace Speckle.Connectors.CSiShared.HostApp;

public class CSiSharedDocumentModelStore : DocumentModelStore
{
  public CSiSharedDocumentModelStore(IJsonSerializer jsonSerializerSettings)
    : base(jsonSerializerSettings, true) { }

  public override void WriteToFile() { }

  public override void ReadFromFile() { }
}
