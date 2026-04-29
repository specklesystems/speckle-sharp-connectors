using Speckle.Objects;
using Speckle.Objects.Other;
using Speckle.Sdk.Models;

namespace Speckle.Converters.AutocadShared.ToSpeckle;

public static class DataObjectDisplayValueExtractor
{
  public static (List<Base> displayValue, RawEncoding? rawEncoding) Extract(Base rawGeometry)
  {
    if (rawGeometry is IDisplayValue<List<SOG.Mesh>> hasDisplay && rawGeometry is SOG.IRawEncodedObject rawEncoded)
    {
      return (hasDisplay.displayValue.Cast<Base>().ToList(), rawEncoded.encodedValue);
    }
    if (rawGeometry is IDisplayValue<List<SOG.Mesh>> hasDisplayMeshes)
    {
      return (hasDisplayMeshes.displayValue.Cast<Base>().ToList(), null);
    }
    return ([rawGeometry], null);
  }
}
