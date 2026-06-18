using Microsoft.Extensions.Logging;
using Speckle.Sdk;
using TSD.API.Remoting.Common;
using TSD.API.Remoting.Materials;
using TSD.API.Remoting.Structure;

namespace Speckle.Converters.TSDShared;

public sealed class TsdWallPropertyExtractor
{
  private readonly ILogger<TsdWallPropertyExtractor> _logger;

  public TsdWallPropertyExtractor(ILogger<TsdWallPropertyExtractor> logger)
  {
    _logger = logger;
  }

  public Dictionary<string, object?> Extract(IStructuralWall wall, IReadOnlyList<IStructuralWallPanel> panels)
  {
    IStructuralWallData? data = null;
    try
    {
      data = wall.StructuralWallData.Value;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogDebug(ex, "Failed to read TSD structural wall data");
    }

    var properties = new Dictionary<string, object?>();

    var objectId = new Dictionary<string, object?>();
    TryAdd(objectId, "GUID", () => wall.Id.ToString());
    TryAdd(objectId, "Label", () => wall.Name);
    TryAdd(objectId, "Wall Type", () => data?.StructuralWallType.Value.ToString());
    if (objectId.Count > 0)
    {
      properties["Object ID"] = objectId;
    }

    if (data is not null)
    {
      var design = new Dictionary<string, object?>();
      TryAdd(design, "Material Type", () => data.MaterialType.Value.ToString());
      TryAdd(design, "Fabrication", () => data.Fabrication.Value.ToString());
      TryAdd(design, "Auto Design", () => data.AutoDesign.Value);
      TryAdd(design, "Generate Support", () => data.GenerateSupport.Value);
      if (design.Count > 0)
      {
        properties["Design"] = design;
      }
    }

    var mesh = new Dictionary<string, object?>();
    TryAdd(mesh, "Mesh Type", () => wall.MeshType.Value.ToString());
    AddDimension(mesh, "Horizontal Size", () => wall.MeshHorizontalSize.Value);
    AddDimension(mesh, "Vertical Size", () => wall.MeshVerticalSize.Value);
    if (mesh.Count > 0)
    {
      properties["Mesh"] = mesh;
    }

    var panelProperties = new Dictionary<string, object?>();
    int index = 1;
    foreach (var panel in panels)
    {
      var extracted = ExtractPanel(panel);
      if (extracted.Count > 0)
      {
        panelProperties[$"Panel {index}"] = extracted;
      }

      index++;
    }

    if (panelProperties.Count > 0)
    {
      properties["Panels"] = panelProperties;
    }

    return properties;
  }

  private Dictionary<string, object?> ExtractPanel(IStructuralWallPanel panel)
  {
    IStructuralWallPanelData? data = null;
    try
    {
      data = panel.WallPanelData.Value;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogDebug(ex, "Failed to read TSD wall panel data");
    }

    var properties = new Dictionary<string, object?>();
    if (data is not null)
    {
      AddDimension(properties, "Thickness", () => data.Thickness.Value);
      AddDimension(properties, "Nominal Cover", () => data.NominalCover.Value);
      TryAdd(properties, "Concrete Type", () => data.ConcreteType.Value.ToString());
      TryAdd(properties, "Alignment", () => data.Alignment.Value.ToString());
      TryAdd(properties, "Reinforcement Layers", () => data.ReinforcementLayers.Value);
      TryAdd(properties, "Material", () => ExtractMaterial(data.Material.Value));
    }

    return properties;
  }

  private static Dictionary<string, object?>? ExtractMaterial(IMaterial? material)
  {
    if (material is null)
    {
      return null;
    }

    return new Dictionary<string, object?> { ["Name"] = material.Name, ["Type"] = material.Type.ToString() };
  }

  private void AddDimension(Dictionary<string, object?> target, string key, Func<double> getter)
  {
    if (TryGetValue(() => (double?)getter()) is double value)
    {
      target[key] = new TsdQuantityValue(key, value, Quantity.Dimension, "mm");
    }
  }

  private void TryAdd(Dictionary<string, object?> target, string key, Func<object?> getter)
  {
    try
    {
      var value = getter();
      if (value is not null)
      {
        target[key] = value;
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogDebug(ex, "Failed to extract TSD wall property {Key}", key);
    }
  }

  private T? TryGetValue<T>(Func<T?> getter)
    where T : struct
  {
    try
    {
      return getter();
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      return null;
    }
  }
}
