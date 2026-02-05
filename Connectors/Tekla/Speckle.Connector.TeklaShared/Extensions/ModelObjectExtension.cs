namespace Speckle.Connectors.TeklaShared.Extensions;

public static class ModelObjectExtensions
{
  private static readonly IReadOnlyList<Type> s_excludedTypes = new[]
  {
    typeof(TSM.ControlPoint),
    typeof(TSM.Weld),
    typeof(TSM.Fitting),
    typeof(TSM.BooleanPart)
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
