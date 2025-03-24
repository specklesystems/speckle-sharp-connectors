using Autodesk.Revit.DB;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Objects;

namespace Speckle.Converters.RevitShared.ToHost.TopLevel;

public class RegionConverterToHost : ITypedConverter<SOG.Region, List<DB.GeometryObject>>
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

    // find all possible suitable views, starting from the Active view
    List<View> suitableViews = new();
    if (_converterSettings.Current.Document.ActiveView.ViewType == ViewType.FloorPlan)
    {
      suitableViews.Add(_converterSettings.Current.Document.ActiveView);
    }

    using var viewCollector = new FilteredElementCollector(_converterSettings.Current.Document);
    viewCollector.OfClass(typeof(View));
    foreach (Element viewElement in viewCollector)
    {
      View view = (View)viewElement;
      if (view.ViewType == ViewType.FloorPlan)
      {
        suitableViews.Add(view);
      }
    }

    // get FilledRegionType from the document, to create a new filled region element
    using var filledRegionCollector = new FilteredElementCollector(_converterSettings.Current.Document);
    Element? filledRegionElementType = filledRegionCollector.OfClass(typeof(DB.FilledRegionType)).FirstElement();

    if (filledRegionElementType != null)
    {
      foreach (var view in suitableViews)
      {
        try
        {
          using FilledRegion filledRegion = FilledRegion.Create(
            _converterSettings.Current.Document,
            filledRegionElementType.Id,
            view.Id,
            profileLoops
          );
          break;
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException) { }
      }
    }

    // return empty list, because FilledRegion is not a GeometryObject
    return resultList;
  }
}
