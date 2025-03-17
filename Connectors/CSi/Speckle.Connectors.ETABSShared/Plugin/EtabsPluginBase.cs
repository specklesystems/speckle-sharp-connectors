using Speckle.Connectors.CSiShared;

namespace Speckle.Connectors.ETABSShared;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "<Pending>")]
public abstract class EtabsPluginBase : CSiPluginBase
{
  public override int Info(ref string text)
  {
    text = "Next Gen Speckle Connector for ETABS";
    return 0;
  }

  protected override SpeckleFormBase CreateForm() => CreateEtabsForm();

  protected abstract EtabsSpeckleFormBase CreateEtabsForm();
}
