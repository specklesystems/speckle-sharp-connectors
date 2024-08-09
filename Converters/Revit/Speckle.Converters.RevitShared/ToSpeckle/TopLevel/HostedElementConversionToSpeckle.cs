using Autodesk.Revit.DB;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Sdk.Models;

namespace Speckle.Converters.RevitShared.ToSpeckle;

[Obsolete("Do not use while as we're rethinking hosted element relationships. This class will most likely go away.")]
public class HostedElementConversionToSpeckle
{
  private readonly IRootToSpeckleConverter _converter;
  private readonly IRevitConversionContextStack _contextStack;

  public HostedElementConversionToSpeckle(IRootToSpeckleConverter converter, IRevitConversionContextStack contextStack)
  {
    _converter = converter;
    _contextStack = contextStack;
  }

  public IEnumerable<Base> ConvertHostedElements(IEnumerable<ElementId> hostedElementIds)
  {
    foreach (var elemId in hostedElementIds)
    {
      Element element = _contextStack.Current.Document.GetElement(elemId);

      Base @base;
      try
      {
        @base = _converter.Convert(element);
      }
      catch (SpeckleConversionException)
      {
        // POC: logging
        continue;
      }

      yield return @base;
    }
  }
}
