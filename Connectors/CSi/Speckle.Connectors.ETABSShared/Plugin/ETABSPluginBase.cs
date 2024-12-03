using Speckle.Connectors.CSiShared;

namespace Speckle.Connectors.ETABSShared;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "<Pending>")]
public abstract class ETABSPluginBase : CSiPluginBase
{
  public override int Info(ref string text)
  {
    text = "Hey Speckler! This is our next-gen ETABS Connector.";
    return 0;
  }
  
  protected override SpeckleFormBase CreateForm() => CreateETABSForm();

  protected abstract ETABSSpeckleFormBase CreateETABSForm();
}
