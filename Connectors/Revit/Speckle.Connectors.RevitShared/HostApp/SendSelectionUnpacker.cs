using Autodesk.Revit.DB;
using Speckle.Converters.RevitShared.Helpers;

namespace Speckle.Connectors.Revit.HostApp;

public class SendSelectionUnpacker
{
  private readonly IRevitConversionContextStack _contextStack;
  private readonly List<Element> _elements = new();

  public SendSelectionUnpacker(IRevitConversionContextStack contextStack)
  {
    _contextStack = contextStack;
  }

  public IEnumerable<Element> UnpackSelection(IEnumerable<Element> selectionElements)
  {
    foreach (var element in selectionElements)
    {
      if (element is Group g)
      {
        UnpackGroup(g);
        continue;
      }
      _elements.Add(element);
    }
    return _elements;
  }

  private void UnpackGroup(Group group)
  {
    var groupElements = group.GetMemberIds().Select(_contextStack.Current.Document.GetElement);
    foreach (var element in groupElements)
    {
      if (element is Group g)
      {
        UnpackGroup(g);
        continue;
      }
      _elements.Add(element);
    }
  }
}
