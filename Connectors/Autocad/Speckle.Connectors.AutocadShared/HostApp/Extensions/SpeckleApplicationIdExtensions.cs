using Autodesk.AutoCAD.DatabaseServices;
using AutocadColor = Autodesk.AutoCAD.Colors.Color;

namespace Speckle.Connectors.Autocad.HostApp.Extensions;

public static class SpeckleApplicationIdExtensions
{
  /// <summary>
  /// Retrieves the Speckle object application id
  /// </summary>
  public static string GetSpeckleApplicationId(this Entity entity) => entity.Handle.ToString();

  /// <summary>
  /// Layers and geometries can have same application ids.....
  /// We should prevent it for sketchup converter. Because when it happens "objects_to_bake" definition
  /// is changing on the way if it happens.
  /// </summary>
  public static string GetSpeckleApplicationId(this LayerTableRecord layerTableRecord) =>
    $"layer_{layerTableRecord.Handle}";

  /// <summary>
  /// Retrieves a unique material Speckle object application id.
  /// </summary>
  /// <remarks> Unconfirmed, but materials and geometries may have same application ids.</remarks>
  public static string GetSpeckleApplicationId(this Material material) => $"material_{material.Handle}";

  /// <summary>
  /// Retrieves a unique color Speckle object application id.
  /// </summary>
  /// <remarks> Assumes color names are unique. </remarks>
  public static string GetSpeckleApplicationId(this AutocadColor color) => $"color_{color.ColorNameForDisplay}";

  /// <summary>
  /// Retrieves a unique group Speckle object application id.
  /// </summary>
  /// <remarks>Unconfirmed, but groups and geometries may have same application ids.</remarks>
  public static string GetSpeckleApplicationId(this Group group) => $"group_{group.Handle}";
}
