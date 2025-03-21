using Autodesk.Revit.DB;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Objects;

namespace Speckle.Converters.RevitShared.ToHost.TopLevel;

public class RegionConverterToHost : ITypedConverter<SOG.Region, List<DB.GeometryObject>>
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly ITypedConverter<ICurve, DB.CurveArray> _curveConverter;
  private readonly RevitToHostCacheSingleton _revitToHostCacheSingleton;
  private readonly ScalingServiceToHost _scalingServiceToHost;

  public RegionConverterToHost(
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    ITypedConverter<ICurve, DB.CurveArray> curveConverter,
    RevitToHostCacheSingleton revitToHostCacheSingleton,
    ScalingServiceToHost scalingServiceToHost
  )
  {
    _converterSettings = converterSettings;
    _curveConverter = curveConverter;
    _revitToHostCacheSingleton = revitToHostCacheSingleton;
    _scalingServiceToHost = scalingServiceToHost;
  }

  public List<DB.GeometryObject> Convert(SOG.Region target)
  {
    List<DB.GeometryObject> resultList = new();

    List<DB.Curve> outerLoop = _curveConverter.Convert(target.boundary).Cast<DB.Curve>().ToList();
    List<List<DB.Curve>> innerLoops = target
      .innerLoops.Select(x => _curveConverter.Convert(x).Cast<DB.Curve>().ToList())
      .ToList();

    // Collect native loops for the filled region into 1 list
    List<CurveLoop> profileLoops = new();

    // Collect boundary curves into a loop
    CurveLoop boundaryLoop = new();
    outerLoop.ForEach(x => boundaryLoop.Append(x));
    profileLoops.Add(boundaryLoop);

    // Collect each of inner curves into a loop
    foreach (var innerLoop in innerLoops)
    {
      CurveLoop voidLoop = new();
      innerLoop.ForEach(x => voidLoop.Append(x));
      profileLoops.Add(voidLoop);
    }

    // Seems to be no way to create a brand new FilledRegion, we need to collect the available ones from doc
    // https://thebuildingcoder.typepad.com/blog/2013/07/create-a-filled-region-to-use-as-a-mask.html
    // https://learnrevitapi.com/blog/create-filled-region-type
    using var collector = new FilteredElementCollector(_converterSettings.Current.Document);
    Element? filledRegionCollector = collector.OfClass(typeof(DB.FilledRegionType)).FirstElement();

    if (filledRegionCollector != null)
    //if (FilledRegion.IsValidFilledRegionTypeId(_converterSettings.Current.Document, filledRegionElement.Id))
    {
      ElementId activeViewId = _converterSettings.Current.Document.ActiveView.Id;
      using FilledRegion filledRegion = FilledRegion.Create(
        _converterSettings.Current.Document,
        filledRegionCollector.Id,
        activeViewId,
        profileLoops
      );
      //break;
      //}
    }

    return resultList;
  }
}
