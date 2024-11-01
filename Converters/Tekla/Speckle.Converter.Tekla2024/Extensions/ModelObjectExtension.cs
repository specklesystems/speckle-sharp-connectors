// this extension method is copy of the same method from connector
// we are using it for to make sure we are traversing children in the same way

namespace Speckle.Converter.Tekla2024.Extensions;

public static class ModelObjectExtensions
{
  public static IEnumerable<TSM.ModelObject> GetSupportedChildren(this TSM.ModelObject modelObject)
  {
    foreach (TSM.ModelObject childObject in modelObject.GetChildren())
    {
      if (childObject is not TSM.ControlPoint or TSM.Weld or TSM.Fitting)
      {
        yield return childObject;
      }
    }
  }
}
