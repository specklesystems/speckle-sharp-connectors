using Speckle.Connectors.CSiShared.HostApp;
using Speckle.Connectors.CSiShared.Utils;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Connectors.CSiShared.Bindings;

public class CsiSharedSelectionBinding(IBrowserBridge parent, ICsiApplicationService csiApplicationService)
  : ISelectionBinding
{
  public string Name => "selectionBinding";
  public IBrowserBridge Parent { get; } = parent;

  /// <summary>
  /// Gets the selection and creates an encoded ID (objectType and objectName).
  /// </summary>
  /// <remarks>
  /// Refer to ObjectIdentifier.cs for more info.
  /// </remarks>
  public SelectionInfo GetSelection()
  {
    int numberItems = 0;
    int[] objectType = [];
    string[] objectName = [];

    csiApplicationService.SapModel.SelectObj.GetSelected(ref numberItems, ref objectType, ref objectName);

    var encodedIds = new List<string>(numberItems);
    var typeCounts = new Dictionary<string, int>();

    for (int i = 0; i < numberItems; i++)
    {
      var typeKey = (ModelObjectType)objectType[i];
      var typeName = typeKey.ToString();

      encodedIds.Add(ObjectIdentifier.Encode(objectType[i], objectName[i]));
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
