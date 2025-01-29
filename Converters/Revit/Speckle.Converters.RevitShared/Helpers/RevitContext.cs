using Autodesk.Revit.UI;

namespace Speckle.Converters.RevitShared.Helpers;

public interface IRevitContext
{
  public UIApplication UIApplication { get; }
}
