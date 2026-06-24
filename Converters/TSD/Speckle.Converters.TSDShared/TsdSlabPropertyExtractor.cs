using Microsoft.Extensions.Logging;
using Speckle.Sdk;
using TSD.API.Remoting.Common;
using TSD.API.Remoting.Materials;
using TSD.API.Remoting.Structure;

namespace Speckle.Converters.TSDShared;

public sealed class TsdSlabPropertyExtractor
{
  private readonly ILogger<TsdSlabPropertyExtractor> _logger;

  public TsdSlabPropertyExtractor(ILogger<TsdSlabPropertyExtractor> logger)
  {
    _logger = logger;
  }

  public Dictionary<string, object?> Extract(ISlabItem slabItem, ISlabData? slabData)
  {
    ISlabItemData? data = null;
    try
    {
      data = slabItem.SlabItemData.Value;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogDebug(ex, "Failed to read TSD slab item data");
    }

    var properties = new Dictionary<string, object?>();

    var objectId = new Dictionary<string, object?>();
    TryAdd(objectId, "GUID", () => slabItem.Id.ToString());
    TryAdd(objectId, "Label", () => slabItem.Name);
    TryAdd(objectId, "Slab Index", () => slabItem.SlabIndex.Value);
    TryAdd(objectId, "Is Overhang", () => slabItem.IsOverhang.Value);
    TryAdd(objectId, "Is Column Drop", () => slabItem.IsColumnDrop.Value);
    if (objectId.Count > 0)
    {
      properties["Object ID"] = objectId;
    }

    if (data is not null)
    {
      var geometry = new Dictionary<string, object?>();
      AddDimension(geometry, "Depth", () => data.Depth.Value);
      AddDimension(geometry, "Top Cover", () => data.TopCover.Value);
      AddDimension(geometry, "Bottom Cover", () => data.BottomCover.Value);
      AddDimension(geometry, "Vertical Offset", () => data.VerticalOffset.Value);
      if (geometry.Count > 0)
      {
        properties["Geometry"] = geometry;
      }

      var design = new Dictionary<string, object?>();
      TryAdd(design, "Auto Design", () => data.AutoDesign.Value);
      TryAdd(design, "Auto Design Option", () => data.AutoDesignOption.Value.ToString());
      if (design.Count > 0)
      {
        properties["Design"] = design;
      }
    }

    if (slabData is not null)
    {
      var material = new Dictionary<string, object?>();
      TryAdd(material, "Slab Type", () => slabData.SlabType.Value.ToString());
      TryAdd(material, "Concrete Type", () => slabData.ConcreteType.Value.ToString());
      TryAdd(material, "Material", () => ExtractMaterial(slabData.Material.Value));
      if (material.Count > 0)
      {
        properties["Material"] = material;
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
      _logger.LogDebug(ex, "Failed to extract TSD slab property {Key}", key);
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
