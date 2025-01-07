using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Utils;

namespace Speckle.Connectors.DUI.Testing;

public class TestDocumentModelStore(IJsonSerializer serializer) : DocumentModelStore(serializer)
{
  protected override void HostAppSaveState(string modelCardState) { }

  protected override void LoadState() { }
}
