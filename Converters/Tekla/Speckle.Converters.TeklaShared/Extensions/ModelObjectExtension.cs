// this extension method is copy of the same method from connector
// we are using it for to make sure we are traversing children in the same way

namespace Speckle.Converters.TeklaShared.Extensions;

public static class ModelObjectExtensions
{
  private static readonly IReadOnlyList<Type> s_excludedTypes = new[]
  {
    typeof(TSM.ControlPoint),
    typeof(TSM.Weld),
    typeof(TSM.Fitting),
  };

  public static IEnumerable<TSM.ModelObject> GetSupportedChildren(this TSM.ModelObject modelObject)
  {
    foreach (TSM.ModelObject childObject in modelObject.GetChildren())
    {
      if (!s_excludedTypes.Contains(childObject.GetType()))
      {
        yield return childObject;
      }
    }
  }
}
