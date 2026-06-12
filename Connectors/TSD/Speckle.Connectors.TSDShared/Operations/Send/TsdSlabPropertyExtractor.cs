using Microsoft.Extensions.Logging;
using Speckle.Sdk;
using TSD.API.Remoting.Common;
using TSD.API.Remoting.Structure;

namespace Speckle.Connectors.TSDShared.Operations.Send;

internal sealed class TsdSlabPropertyExtractor
{
  private readonly ILogger<TsdSlabPropertyExtractor> _logger;

  public TsdSlabPropertyExtractor(ILogger<TsdSlabPropertyExtractor> logger)
  {
    _logger = logger;
  }

  public Dictionary<string, object?> Extract(ISlabItem slabItem)
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

    return properties;
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
