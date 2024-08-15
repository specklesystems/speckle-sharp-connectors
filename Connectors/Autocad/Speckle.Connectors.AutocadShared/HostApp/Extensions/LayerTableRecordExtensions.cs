using Autodesk.AutoCAD.DatabaseServices;

namespace Speckle.Connectors.Autocad.HostApp.Extensions;

public static class LayerTableRecordExtensions
{
  /// <summary>
  /// Layers and geometries can have same application ids.....
  /// We should prevent it for sketchup converter. Because when it happens "objects_to_bake" definition
  /// is changing on the way if it happens.
  /// </summary>
  public static string GetSpeckleApplicationId(this LayerTableRecord layerTableRecord) =>
    $"layer_{layerTableRecord.Handle}";
}
