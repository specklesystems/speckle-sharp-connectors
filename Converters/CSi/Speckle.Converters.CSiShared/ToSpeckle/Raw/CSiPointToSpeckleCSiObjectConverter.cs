using Speckle.Converters.Common.Objects;
using Speckle.Objects.Geometry;

namespace Speckle.Converters.CSiShared.ToSpeckle.Raw
{
  public class CSiPointToSpeckleCSiObjectConverter : ITypedConverter<CSiPointWrapper, CSiObject>
  {
    private ITypedConverter<CSiPointWrapper, Point> _pointConverter;

    public CSiPointToSpeckleCSiObjectConverter(ITypedConverter<CSiPointWrapper, Point> converter)
    {
      _pointConverter = converter;
    }

    public CSiObject Convert(CSiPointWrapper source)
    {
      var point = _pointConverter.Convert(source); // Point is speckle point for displayvalue of csi object

      CSiObject cSiObject = new CSiObject(); // TODO: Finish implementation
    }
  }
}
