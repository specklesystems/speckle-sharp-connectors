using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Utils;

namespace Speckle.Connectors.CSiShared.HostApp;

public class CSiSharedDocumentModelStore : DocumentModelStore
{
  public CSiSharedDocumentModelStore(IJsonSerializer jsonSerializerSettings)
    : base(jsonSerializerSettings) { }

  protected override void HostAppSaveState(string modelCardState) => throw new NotImplementedException();

  protected override void LoadState() => throw new NotImplementedException();
}
