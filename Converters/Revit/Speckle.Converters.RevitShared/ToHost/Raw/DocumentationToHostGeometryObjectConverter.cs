using Speckle.Converters.Common.Objects;
using Speckle.Objects.Data;
using Speckle.Sdk;
using Speckle.Sdk.Models;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class DocumentationToHostGeometryObjectConverter : ITypedConverter<Base, object>
{
  private readonly ITypedConverter<Base, List<DB.GeometryObject>> _baseToGeometryConverter;
  private readonly ITypedConverter<SOG.Region, string> _regionToFilledRegionConverter;

  public DocumentationToHostGeometryObjectConverter(
    ITypedConverter<Base, List<DB.GeometryObject>> baseToGeometryConverter,
    ITypedConverter<SOG.Region, string> regionToFilledRegionConverter
  )
  {
    _baseToGeometryConverter = baseToGeometryConverter;
    _regionToFilledRegionConverter = regionToFilledRegionConverter;
  }

  public object Convert(Base target)
  {
    switch (target)
    {
      case SOG.Region region:
        try
        {
          // try documentation converter for an individual Region
          return new List<string>() { _regionToFilledRegionConverter.Convert(region) };
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
          return _baseToGeometryConverter.Convert(region);
        }

      case DataObject dataObj:
        if (dataObj.displayValue.Any(x => x is not SOG.Region))
        {
          return _baseToGeometryConverter.Convert(target);
        }

        try
        {
          // try documentation converter for all displayValue Regions
          return dataObj.displayValue.Select(x => _regionToFilledRegionConverter.Convert((SOG.Region)x)).ToList();
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
          return _baseToGeometryConverter.Convert(target);
        }

      default:
        return _baseToGeometryConverter.Convert(target);
    }
  }
}
