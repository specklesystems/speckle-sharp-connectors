using Speckle.Connectors.CSiShared.HostApp;
using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.ETABSShared.HostApp;

public class ETABSSendCollectionManager : CSiSendCollectionManager
{
    public ETABSSendCollectionManager(IConverterSettingsStore<CSiConversionSettings> converterSettings) 
        : base(converterSettings)
    {
    }

    public override Collection AddObjectCollectionToRoot(Base convertedObject, Collection rootObject)
    {
        var properties = convertedObject["properties"] as Dictionary<string, object>;
        string story = GetStoryName(properties);
        string objectType = GetObjectType(convertedObject, properties);

        var storyCollection = GetOrCreateStoryCollection(story, rootObject);
        
        var typeCollection = GetOrCreateTypeCollection(objectType, storyCollection);

        return typeCollection;
    }

    private Collection GetOrCreateStoryCollection(string story, Collection rootObject)
    {
        string storyPath = $"Story_{story}";
        if (CollectionCache.TryGetValue(storyPath, out Collection? existingCollection))
        {
            return existingCollection;
        }

        var storyCollection = new Collection(story);
        rootObject.elements.Add(storyCollection);
        CollectionCache[storyPath] = storyCollection;
        return storyCollection;
    }

    private Collection GetOrCreateTypeCollection(string objectType, Collection storyCollection)
    {
        string typePath = $"{storyCollection["name"]}_{objectType}";
        if (CollectionCache.TryGetValue(typePath, out Collection? existingCollection))
        {
            return existingCollection;
        }

        var typeCollection = new Collection(objectType);
        storyCollection.elements.Add(typeCollection);
        CollectionCache[typePath] = typeCollection;
        return typeCollection;
    }

    private string GetObjectType(Base convertedObject, Dictionary<string, object>? properties)
    {
        string baseType = convertedObject["type"]?.ToString() ?? "Unknown";
        
        if (baseType == "Frame" && properties != null)
        {
            if (properties.TryGetValue("designOrientation", out var orientation))
            {
                return orientation?.ToString() switch
                {
                    "Column" => "Columns",
                    "Beam" => "Beams",
                    "Brace" => "Braces",
                    "Null" => "Null",
                    _ => "Frames (Other)"
                };
            }
        }
        
        else if (baseType == "Shell" && properties != null)
        {
          if (properties.TryGetValue("designOrientation", out var orientation))
          {
            return orientation?.ToString() switch
            {
              "Wall" => "Walls",
              "Floor" => "Floors",
              "Ramp_DO_NOT_USE" => "Ramps",
              "Null" => "Null",
              _ => "Shells (Other)"
            };
          }
        }
        
        return baseType;
    }

    private string GetStoryName(Dictionary<string, object>? properties)
    {
        if (properties != null && properties.TryGetValue("story", out var story))
        {
            return story?.ToString() ?? "Unassigned";
        }
        return "Unassigned";
    }
}
