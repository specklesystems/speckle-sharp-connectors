using Speckle.Connectors.CSiShared.HostApp;
using Speckle.Connectors.CSiShared.Utils;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.CSiShared.Bindings;

public class CsiSharedSelectionBinding : ISelectionBinding
{
  public string Name => "selectionBinding";
  public IBrowserBridge Parent { get; }
  private readonly ICsiApplicationService _csiApplicationService;

  public CsiSharedSelectionBinding(IBrowserBridge parent, ICsiApplicationService csiApplicationService)
  {
    Parent = parent;
    _csiApplicationService = csiApplicationService;
  }

  /// <summary>
  /// Gets the selection and creates an encoded ID (objectType and objectName).
  /// </summary>
  /// <remarks>
  /// Refer to ObjectIdentifier.cs for more info.
  /// </remarks>
  public SelectionInfo GetSelection()
  {
    // TODO: Since this is standard across CSi Suite - better stored in an enum?
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

      encodedIds.Add(ObjectIdentifier.Encode(typeKey, objectName[i]));
      typeCounts[typeName] = (typeCounts.TryGetValue(typeName, out var count) ? count : 0) + 1; // NOTE: Cross-framework compatibility (net 48 and net8)
    }

    var summary =
      encodedIds.Count == 0
        ? "No objects selected."
        : $"{encodedIds.Count} objects ({string.Join(", ", 
            typeCounts.Select(kv => $"{kv.Value} {kv.Key}"))})";

    return new SelectionInfo(encodedIds, summary);
  }
}
