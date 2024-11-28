using Speckle.Connectors.CSiShared.HostApp;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.CSiShared.Bindings;

public class CSiSharedSelectionBinding : ISelectionBinding
{
  public string Name => "selectionBinding";
  public IBrowserBridge Parent { get; }
  private readonly ICSiApplicationService _csiApplicationService; // Update selection binding to centralized CSiSharedApplicationService instead of trying to maintain a reference to "sapModel"

  public CSiSharedSelectionBinding(IBrowserBridge parent, ICSiApplicationService csiApplicationService)
  {
    Parent = parent;
    _csiApplicationService = csiApplicationService;
  }

  public SelectionInfo GetSelection()
  {
    // TODO: Handle better. Enums? ObjectType same in ETABS and SAP
    var objectTypeMap = new Dictionary<int, string>
    {
      { 1, "Point" },
      { 2, "Frame" },
      { 3, "Cable" },
      { 4, "Tendon" },
      { 5, "Area" },
      { 6, "Solid" },
      { 7, "Link" }
    };

    int numberItems = 0;
    int[] objectType = Array.Empty<int>();
    string[] objectName = Array.Empty<string>();

    _csiApplicationService.SapModel.SelectObj.GetSelected(ref numberItems, ref objectType, ref objectName);

    var encodedIds = new List<string>(numberItems);
    var typeCounts = new Dictionary<string, int>();

    for (int i = 0; i < numberItems; i++)
    {
      var typeKey = objectType[i];
      var typeName = objectTypeMap.TryGetValue(typeKey, out var name) ? name : $"Unknown ({typeKey})";

      encodedIds.Add(EncodeObjectIdentifier(typeKey, objectName[i]));
      typeCounts[typeName] = (typeCounts.TryGetValue(typeName, out var count) ? count : 0) + 1; // NOTE: Cross-framework compatibility
    }

    var summary =
      encodedIds.Count == 0
        ? "No objects selected."
        : $"{encodedIds.Count} objects ({string.Join(", ", 
            typeCounts.Select(kv => $"{kv.Value} {kv.Key}"))})";

    return new SelectionInfo(encodedIds, summary);
  }

  // NOTE: All API methods are based on the objectType and objectName, not the GUID
  // We will obviously manage the GUIDs but for all method calls we need a concatenated version of the objectType and objectName
  // Since objectType >= 1 and <= 7, we know first index will always be the objectType
  // Remaining string represents objectName and since the user can add any string (provided it is unique), this is safer
  // than using a delimiting character (which could clash with user string)
  private string EncodeObjectIdentifier(int objectType, string objectName)
  {
    // Just in case some weird objectType pops up
    if (objectType < 1 || objectType > 7)
    {
      throw new ArgumentException($"Invalid object type: {objectType}. Must be between 1 and 7.");
    }

    // Simply prepend the object type as a single character
    return $"{objectType}{objectName}";
  }
}
