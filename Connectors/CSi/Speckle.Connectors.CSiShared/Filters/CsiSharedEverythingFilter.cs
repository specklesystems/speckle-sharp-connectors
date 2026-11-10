using Speckle.Connectors.CSiShared.HostApp;
using Speckle.Connectors.CSiShared.Utils;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Utils;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Connectors.CSiShared.Filters;

public class CsiSharedEverythingFilter : DiscriminatedObject, ISendFilter
{
  private delegate int NameList(ref int count, ref string[] names);

  private readonly ICsiApplicationService _csiApplicationService;

  public string Id { get; set; } = "everything";
  public string Type { get; set; } = "Everything";
  public string Name { get; set; } = "Everything";
  public string? Summary { get; set; } = "All supported objects in the model";
  public bool IsDefault { get; set; }
  public List<string> SelectedObjectIds { get; set; } = [];
  public Dictionary<string, string>? IdMap { get; set; }

  public CsiSharedEverythingFilter(ICsiApplicationService csiApplicationService)
  {
    _csiApplicationService = csiApplicationService;
  }

  public List<string> RefreshObjectIds()
  {
    var model = _csiApplicationService.SapModel;
    SelectedObjectIds =
    [
      .. GetNames(ModelObjectType.JOINT, model.PointObj.GetNameList),
      .. GetNames(ModelObjectType.FRAME, model.FrameObj.GetNameList),
      .. GetNames(ModelObjectType.SHELL, model.AreaObj.GetNameList),
    ];
    return SelectedObjectIds;
  }

  private static IEnumerable<string> GetNames(ModelObjectType objectType, NameList getNameList)
  {
    int count = 0;
    string[] names = [];
    getNameList(ref count, ref names);
    return names.Take(count).Select(name => ObjectIdentifier.Encode((int)objectType, name));
  }
}
