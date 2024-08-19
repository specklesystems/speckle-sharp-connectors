using Rhino.DocObjects;

namespace Speckle.Connectors.Rhino.Extensions;

public static class SpeckleApplicationIdExtensions
{
  /// <summary>
  /// Retrieves the Speckle application id
  /// </summary>
  public static string GetSpeckleApplicationId(this RhinoObject rhinoObj) => rhinoObj.Id.ToString();

  /// <summary>
  /// Retrieves a unique Speckle application id from the rgb value and color source.
  /// </summary>
  /// <remarks> Uses the rgb value and source since color names are not unique </remarks>
  public static string GetSpeckleApplicationId(this Color color, ObjectColorSource source) =>
    $"color_{color}_{(source is ObjectColorSource.ColorFromParent ? "block" : source is ObjectColorSource.ColorFromLayer ? "layer" : source is ObjectColorSource.ColorFromMaterial ? "material" : "object")}";
}
