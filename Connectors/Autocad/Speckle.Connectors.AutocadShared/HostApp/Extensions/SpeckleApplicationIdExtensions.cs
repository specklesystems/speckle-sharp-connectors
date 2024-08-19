using Autodesk.AutoCAD.DatabaseServices;
using AutocadColor = Autodesk.AutoCAD.Colors.Color;

namespace Speckle.Connectors.Autocad.HostApp.Extensions;

public static class SpeckleApplicationIdExtensions
{
  /// <summary>
  /// Retrieves the Speckle object application id
  /// </summary>
  public static string GetSpeckleApplicationId(this Entity entity) => entity.Handle.Value.ToString();

  /// <summary>
  /// Retrieves the Speckle object application id
  /// </summary>
  public static string GetSpeckleApplicationId(this DBObject dbObj) => dbObj.Handle.Value.ToString();

  /// <summary>
  /// Layers and geometries can have same application ids.....
  /// We should prevent it for sketchup converter. Because when it happens "objects_to_bake" definition
  /// is changing on the way if it happens.
  /// </summary>
  public static string GetSpeckleApplicationId(this LayerTableRecord layerTableRecord) =>
    $"layer_{layerTableRecord.Handle.Value}";

  /// <summary>
  /// Retrieves a unique material Speckle object application id.
  /// </summary>
  /// <remarks> Unconfirmed, but materials and geometries may have same application ids.</remarks>
  public static string GetSpeckleApplicationId(this Material material) => $"material_{material.Handle.Value}";

  /// <summary>
  /// Retrieves a unique color Speckle object application id from the rgb value and color source.
  /// </summary>
  /// <remarks> Uses the rgb value since color names are not unique </remarks>
  public static string GetSpeckleApplicationId(this AutocadColor color) =>
    $"color_{color.ColorValue}_{(color.IsByBlock ? "block" : color.IsByLayer ? "layer" : "object")}";

  /// <summary>
  /// Retrieves a unique group Speckle object application id.
  /// </summary>
  /// <remarks>Unconfirmed, but groups and geometries may have same application ids.</remarks>
  public static string GetSpeckleApplicationId(this Group group) => $"group_{group.Handle.Value}";
}
