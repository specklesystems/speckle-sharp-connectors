using Speckle.Connectors.DUI.Models.Card.SendFilter;

namespace Speckle.Connectors.Rhino.Filters;

public class RhinoEverythingFilter : EverythingSendFilter
{
  public override List<string> GetObjectIds() => new(); // TODO

  public override bool CheckExpiry(string[] changedObjectIds) => true;
}
