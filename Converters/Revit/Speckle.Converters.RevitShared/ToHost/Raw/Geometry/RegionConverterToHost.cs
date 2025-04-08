using Autodesk.Revit.DB;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Objects;

namespace Speckle.Converters.RevitShared.ToHost.TopLevel;

public class RegionConverterToHost : ITypedConverter<SOG.Region, string>
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly ITypedConverter<ICurve, DB.CurveArray> _curveConverter;

  public RegionConverterToHost(
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    ITypedConverter<ICurve, DB.CurveArray> curveConverter
  )
  {
    _converterSettings = converterSettings;
    _curveConverter = curveConverter;
  }

  public string Convert(SOG.Region target)
  {
    List<DB.GeometryObject> resultList = new();
    List<CurveLoop> profileLoops = new();

    // convert boundary loop and add to profileLoops list
    CurveLoop boundaryLoop = new();
    List<DB.Curve> outerLoop = _curveConverter.Convert(target.boundary).Cast<DB.Curve>().ToList();
    outerLoop.ForEach(x => boundaryLoop.Append(x));
    profileLoops.Add(boundaryLoop);

    // convert inner loops and add to profileLoops list
    List<List<DB.Curve>> innerLoops = target
      .innerLoops.Select(x => _curveConverter.Convert(x).Cast<DB.Curve>().ToList())
      .ToList();
    foreach (var innerLoop in innerLoops)
    {
      CurveLoop voidLoop = new();
      innerLoop.ForEach(x => voidLoop.Append(x));
      profileLoops.Add(voidLoop);
    }

    // get FilledRegionType from the document to create a new FilledRegion element
    using var filledRegionCollector = new FilteredElementCollector(_converterSettings.Current.Document);
    Element filledRegionElementType = filledRegionCollector.OfClass(typeof(DB.FilledRegionType)).FirstElement();

    // follow the pattern of the native CAD import: try to draw native FilledRegion in the Active view,
    // or draw a linked CAD document, if imported into unsupported View (in our case: don't catch the error, so the converter will default to fallback)
    View activeView = _converterSettings.Current.Document.ActiveView;

    // Autodesk.Revit.Exceptions.ArgumentException will be thrown if ActiveView invalid
    using FilledRegion filledRegion = FilledRegion.Create(
      _converterSettings.Current.Document,
      filledRegionElementType.Id,
      activeView.Id,
      profileLoops
    );

    return filledRegion.UniqueId;
  }
}
