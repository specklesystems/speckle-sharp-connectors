using Autodesk.Revit.DB;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Extensions;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Sdk;

namespace Speckle.Converters.RevitShared.ToSpeckle.Properties;

/// <summary>
/// For retrieving general properties used for business intelligence on the class, that are not already captured in parameters.
/// </summary>
public class ClassPropertiesExtractor
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;

  private readonly Dictionary<int, string> _worksetCache = new();

  public ClassPropertiesExtractor(IConverterSettingsStore<RevitConversionSettings> converterSettings)
  {
    _converterSettings = converterSettings;
  }

  public Dictionary<string, object?> GetClassProperties(DB.Element element)
  {
    Dictionary<string, object?> elementPropertiesDict = ExtractElementProperties(element);

    // type specific properties
    switch (element)
    {
      // area scheme for area elements
      case DB.Area area:
        elementPropertiesDict.Add("areaScheme", area.AreaScheme?.Name);
        break;
    }

    return elementPropertiesDict;
  }

  // gets the properties on the db.element class
  private Dictionary<string, object?> ExtractElementProperties(DB.Element element)
  {
    Dictionary<string, object?> elementProperties =
      new()
      {
        { "elementId", element.Id.ToString() },
        { "builtInCategory", element.Category?.GetBuiltInCategory().ToString() },
        { "worksetId", element.WorksetId?.ToString() },
      };

    int? worksetId = element.WorksetId?.IntegerValue;
    if (worksetId is not null)
    {
      // get workset name
      if (!_worksetCache.TryGetValue(worksetId.Value, out var worksetName))
      {
        // FIX: [CNX-2414] use the element's own document instead of the converter settings document, preventing
        // null reference exceptions when processing elements from linked models where workset exists in linked
        // document but not in the main document
        try
        {
          DB.Workset workset = element.Document.GetWorksetTable().GetWorkset(element.WorksetId);
          worksetName = workset?.Name ?? "Unknown Workset";
          _worksetCache[worksetId.Value] = worksetName;
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
          // fallback: if we can't get the workset for any reason (e.g., workset doesn't exist),
          // provide a safe default instead of crashing (I don't think a workset name prop is cause for a fail!)
          worksetName = "Unknown Workset";
          _worksetCache[worksetId.Value] = worksetName;
        }
      }

      elementProperties.Add("worksetName", worksetName);
    }

    if (element is DB.FamilyInstance familyInstance)
    {
      try
      {
        // get room id if applicable (only for FamilyInstance elements)
        if (familyInstance.Room is not null)
        {
          elementProperties.Add("roomApplicationId", familyInstance.Room.UniqueId.ToString());
        }

        // get space id if applicable (only for FamilyInstance elements)
        if (familyInstance.Space is not null)
        {
          elementProperties.Add("spaceApplicationId", familyInstance.Space.UniqueId.ToString());
        }

        // get toRoom and fromRoom for FamilyInstance elements with those properties (e.g. Doors)
        if (familyInstance.ToRoom is not null)
        {
          elementProperties.Add("toRoomApplicationId", familyInstance.ToRoom.UniqueId.ToString());
        }

        if (familyInstance.FromRoom is not null)
        {
          elementProperties.Add("fromRoomApplicationId", familyInstance.FromRoom.UniqueId.ToString());
        }

        Element? parent = null;

#if REVIT2023_OR_GREATER
        BuiltInCategory bic = familyInstance.Category.BuiltInCategory;
#else
        // Cast for 2022 and older
        BuiltInCategory bic = (BuiltInCategory)familyInstance.Category.Id.IntegerValue;
#endif

        if (bic == BuiltInCategory.OST_CurtainWallMullions || bic == BuiltInCategory.OST_CurtainWallPanels)
        {
          parent = familyInstance.Host;
        }

        parent ??= familyInstance.SuperComponent;

        if (parent != null)
        {
          elementProperties.Add("parentApplicationId", parent.UniqueId);
        }
      }
      catch (Exception e) when (!e.IsFatal())
      {
        // silently ignore - not critical
      }
    }

    // get group name if applicable
    // TODO: in in group proxies separate issue. Below comments from PR #1081
    // We're using group proxies in Rhino etc. Groups should be handled similarly in Revit, unless there's a good
    // reason to deviate. We should prioritize consistency here esp as we shift focus to our dashboarding
    // We've decided to add group proxies as a separate issue, once we are more opinionated on the proxy vs properties
    // consumability in dashboards vs powerbi
    var groupId = element.GroupId;
    if (groupId is not null)
    {
      if (element.Document.GetElement(groupId) is DB.Group group)
      {
        elementProperties.Add("groupName", group.GroupType.Name);
      }
    }

    return elementProperties;
  }
}
