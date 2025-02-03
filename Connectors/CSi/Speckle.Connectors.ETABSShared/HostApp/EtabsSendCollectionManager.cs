using Speckle.Connectors.CSiShared.HostApp;
using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.Utils;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.ETABSShared.HostApp;

/// <summary>
/// ETABS-specific collection manager that organizes structural elements by level and type.
/// Creates a hierarchical structure that mirrors ETABS' native organization.
/// </summary>
public class EtabsSendCollectionManager : CsiSendCollectionManager
{
  private const string DEFAULT_LEVEL = "Unassigned";

  private readonly Dictionary<ElementCategory, string> _categoryNames =
    new()
    {
      { ElementCategory.COLUMN, "Columns" },
      { ElementCategory.BEAM, "Beams" },
      { ElementCategory.BRACE, "Braces" },
      { ElementCategory.WALL, "Walls" },
      { ElementCategory.FLOOR, "Floors" },
      { ElementCategory.RAMP, "Ramps" },
      { ElementCategory.JOINT, "Joints" },
      { ElementCategory.OTHER, "Other" }
    };

  public EtabsSendCollectionManager(IConverterSettingsStore<CsiConversionSettings> converterSettings)
    : base(converterSettings) { }

  public override Collection AddObjectCollectionToRoot(Base convertedObject, Collection rootObject)
  {
    var level = GetObjectLevelFromObject(convertedObject);
    var category = GetElementCategoryFromObject(convertedObject);

    return GetOrCreateCollectionHierarchy(level, category, rootObject);
  }

  private string GetObjectLevelFromObject(Base obj)
  {
    // Properties from converter are stored in "Object ID" dictionary
    // NOTE: Introduce enums for these object keys? I don't like string indexing.
    if (obj["properties"] is not Dictionary<string, object> properties)
    {
      return DEFAULT_LEVEL;
    }

    if (
      properties.TryGetValue(ObjectPropertyCategory.OBJECT_ID, out var objectId)
      && objectId is Dictionary<string, object> parameters
    )
    {
      return parameters.TryGetValue(CommonObjectProperty.LEVEL, out var level)
        ? level?.ToString() ?? DEFAULT_LEVEL
        : DEFAULT_LEVEL;
    }

    return DEFAULT_LEVEL;
  }

  private ElementCategory GetElementCategoryFromObject(Base obj)
  {
    var type = obj["type"]?.ToString();

    // Handle non-structural elements
    if (string.IsNullOrEmpty(type))
    {
      return ElementCategory.OTHER;
    }

    // For frames and shells, get design orientation from Object ID
    if (
      (type == ModelObjectType.FRAME.ToString() || type == ModelObjectType.SHELL.ToString())
      && obj["properties"] is Dictionary<string, object> properties
    )
    {
      if (
        properties.TryGetValue(ObjectPropertyCategory.OBJECT_ID, out var objectId)
        && objectId is Dictionary<string, object> parameters
      )
      {
        if (parameters.TryGetValue(CommonObjectProperty.DESIGN_ORIENTATION, out var orientation))
        {
          return GetCategoryFromDesignOrientation(orientation?.ToString(), type);
        }
      }
    }

    // For joints, simply categorize as joints
    return type == ModelObjectType.JOINT.ToString() ? ElementCategory.JOINT : ElementCategory.OTHER;
  }

  private ElementCategory GetCategoryFromDesignOrientation(string? orientation, string type)
  {
    if (string.IsNullOrEmpty(orientation))
    {
      return ElementCategory.OTHER;
    }

    return (orientation, type) switch
    {
      ("Column", nameof(ModelObjectType.FRAME)) => ElementCategory.COLUMN,
      ("Beam", nameof(ModelObjectType.FRAME)) => ElementCategory.BEAM,
      ("Brace", nameof(ModelObjectType.FRAME)) => ElementCategory.BRACE,
      ("Wall", nameof(ModelObjectType.SHELL)) => ElementCategory.WALL,
      ("Floor", nameof(ModelObjectType.SHELL)) => ElementCategory.FLOOR,
      ("Ramp", nameof(ModelObjectType.SHELL)) => ElementCategory.RAMP,
      _ => ElementCategory.OTHER
    };
  }

  private Collection GetOrCreateCollectionHierarchy(string level, ElementCategory category, Collection root)
  {
    var hierarchyKey = $"{level}_{category}";

    if (CollectionCache.TryGetValue(hierarchyKey, out var existingCollection))
    {
      return existingCollection;
    }

    var levelCollection = GetOrCreateLevelCollection(level, root);
    var categoryCollection = CreateCategoryCollection(category, levelCollection);

    CollectionCache[hierarchyKey] = categoryCollection;
    return categoryCollection;
  }

  private Collection GetOrCreateLevelCollection(string level, Collection root)
  {
    var levelKey = $"Level_{level}";

    if (CollectionCache.TryGetValue(levelKey, out var existingCollection))
    {
      return existingCollection;
    }

    var levelCollection = new Collection(level);
    root.elements.Add(levelCollection);
    CollectionCache[levelKey] = levelCollection;

    return levelCollection;
  }

  private Collection CreateCategoryCollection(ElementCategory category, Collection levelCollection)
  {
    var categoryCollection = new Collection(_categoryNames[category]);
    levelCollection.elements.Add(categoryCollection);
    return categoryCollection;
  }
}
