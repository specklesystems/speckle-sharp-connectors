using Autodesk.Revit.DB;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Connectors.Revit.HostApp;

/// <summary>
/// Bakes all objects into a single top level group and pins it.
/// </summary>
public class RevitGroupBaker : TraversalContextUnpacker
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly RevitUtils _revitUtils;

  public RevitGroupBaker(IConverterSettingsStore<RevitConversionSettings> converterSettings, RevitUtils revitUtils)
  {
    _converterSettings = converterSettings;
    _revitUtils = revitUtils;
  }

  private readonly List<ElementId> _elementIdsForTopLevelGroup = new();

  public void AddToTopLevelGroup(Element revitElement) => _elementIdsForTopLevelGroup.Add(revitElement.Id);

  public void BakeGroupForTopLevel(string baseGroupName)
  {
    var docGroup = _converterSettings.Current.Document.Create.NewGroup(_elementIdsForTopLevelGroup);
    docGroup.GroupType.Name = _revitUtils.RemoveInvalidChars(baseGroupName);
    docGroup.Pinned = true;
  }

  public void PurgeGroups(string baseGroupName)
  {
    var document = _converterSettings.Current.Document;
    var groups = GetGroupsByName(document, baseGroupName);

    foreach (var group in groups)
    {
      var subgroupTypeIds = new List<ElementId>() { group.GroupType.Id };
      CollectSubGroupTypeIds(document, group, subgroupTypeIds);
      document.Delete(subgroupTypeIds);
    }
  }

  private List<Group> GetGroupsByName(Document doc, string groupName)
  {
    var validGroupName = _revitUtils.RemoveInvalidChars(groupName);

    using var collector = new FilteredElementCollector(doc);
    ICollection<Element> groupElements = collector.OfClass(typeof(Group)).ToElements();
    List<Group> groups = groupElements.Cast<Group>().Where(g => g.GroupType.Name == validGroupName).ToList();
    return groups;
  }

  private void CollectSubGroupTypeIds(Document document, Group group, List<ElementId> subGroupTypeIds)
  {
    ICollection<ElementId> groupMemberIds = group.GetMemberIds();

    foreach (ElementId memberId in groupMemberIds)
    {
      Element element = document.GetElement(memberId);

      if (element is Group subgroup)
      {
        subGroupTypeIds.Add(subgroup.GroupType.Id);
        CollectSubGroupTypeIds(document, subgroup, subGroupTypeIds);
      }
    }
  }
}
