using Speckle.Connectors.DUI.Models.Card.SendFilter;

namespace Speckle.Connectors.RevitShared.Operations.Send.Filters;

public class RevitEverythingFilter : EverythingSendFilter
{
  public override List<string> GetObjectIds()
  {
    // TODO
    return new List<string>();
  }

  public override bool CheckExpiry(string[] changedObjectIds)
  {
    return true;
  }
}
