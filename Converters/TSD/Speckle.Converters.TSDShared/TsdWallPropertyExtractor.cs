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

  public Dictionary<string, object?> Extract(IStructuralWall wall)
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

    return properties;
  }

  public Dictionary<string, object?> ExtractPanel(IStructuralWallPanel panel)
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

    var objectId = new Dictionary<string, object?>();
    TryAdd(objectId, "GUID", () => panel.Id.ToString());
    TryAdd(objectId, "Index", () => panel.Index);
    if (objectId.Count > 0)
    {
      properties["Object ID"] = objectId;
    }

    if (data is not null)
    {
      var section = new Dictionary<string, object?>();
      AddDimension(section, "Thickness", () => data.Thickness.Value);
      AddDimension(section, "Nominal Cover", () => data.NominalCover.Value);
      TryAdd(section, "Concrete Type", () => data.ConcreteType.Value.ToString());
      TryAdd(section, "Alignment", () => data.Alignment.Value.ToString());
      TryAdd(section, "Reinforcement Layers", () => data.ReinforcementLayers.Value);
      TryAdd(section, "Material", () => ExtractMaterial(data.Material.Value));
      if (section.Count > 0)
      {
        properties["Section"] = section;
      }
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
