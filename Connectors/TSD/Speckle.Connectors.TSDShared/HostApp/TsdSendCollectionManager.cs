using Speckle.Connectors.TSDShared.Utils;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.TSDShared.HostApp;

internal sealed class TsdSendCollectionManager
{
  private const string DEFAULT_CATEGORY = "Other";

  private static readonly Dictionary<TsdElementCategory, string> s_categoryNames = new()
  {
    { TsdElementCategory.COLUMN, "Columns" },
    { TsdElementCategory.BEAM, "Beams" },
    { TsdElementCategory.BRACE, "Braces" },
    { TsdElementCategory.WALL, "Walls" },
    { TsdElementCategory.FLOOR, "Floors" },
    { TsdElementCategory.OTHER, DEFAULT_CATEGORY },
  };

  private readonly Dictionary<string, Collection> _collectionCache = new();

  public Collection AddObjectCollectionToRoot(Base convertedObject, Collection rootObject)
  {
    var categoryName = s_categoryNames[GetElementCategory(convertedObject)];

    if (_collectionCache.TryGetValue(categoryName, out var existing))
    {
      return existing;
    }

    var collection = new Collection { name = categoryName };
    rootObject.elements.Add(collection);
    _collectionCache[categoryName] = collection;
    return collection;
  }

  private static TsdElementCategory GetElementCategory(Base convertedObject) =>
    convertedObject["type"]?.ToString() switch
    {
      "Column" or "BearingWallColumn" or "WallColumnElement" => TsdElementCategory.COLUMN,
      "Beam" or "BearingWallBeam" or "EavesBeam" or "WallBeamElement" or "WallMeshBeamElement" =>
        TsdElementCategory.BEAM,
      "Brace" => TsdElementCategory.BRACE,
      "StructuralWall" => TsdElementCategory.WALL,
      "Slab" or "SlabItem" => TsdElementCategory.FLOOR,
      _ => TsdElementCategory.OTHER,
    };
}
