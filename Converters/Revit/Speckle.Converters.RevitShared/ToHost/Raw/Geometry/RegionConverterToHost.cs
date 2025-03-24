using Autodesk.Revit.DB;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Objects;
using Speckle.Sdk;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.RevitShared.ToHost.TopLevel;

public class RegionConverterToHost : ITypedConverter<SOG.Region, List<DB.GeometryObject>>
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly ITypedConverter<ICurve, DB.CurveArray> _curveConverter;
  private readonly ITypedConverter<SOG.Mesh, List<DB.GeometryObject>> _meshConverter;

  public RegionConverterToHost(
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    ITypedConverter<ICurve, DB.CurveArray> curveConverter,
    ITypedConverter<SOG.Mesh, List<DB.GeometryObject>> meshConverter
  )
  {
    _converterSettings = converterSettings;
    _curveConverter = curveConverter;
    _meshConverter = meshConverter;
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

    // get FilledRegionType from the document to create a new FilledRegion element
    using var filledRegionCollector = new FilteredElementCollector(_converterSettings.Current.Document);
    Element filledRegionElementType = filledRegionCollector.OfClass(typeof(DB.FilledRegionType)).FirstElement();

    View activeView = _converterSettings.Current.Document.ActiveView;
    try
    {
      using FilledRegion filledRegion = FilledRegion.Create(
        _converterSettings.Current.Document,
        filledRegionElementType.Id,
        activeView.Id,
        profileLoops
      );
    }
    catch (Autodesk.Revit.Exceptions.ArgumentException)
    {
      // follow the pattern of the native CAD import: draw native FilledRegion if imported into 2d View
      // and draw a linked document, if imported into unsupported View (in our case: default to Mesh converter)
      foreach (var displayMesh in target.displayValue)
      {
        var regionMeshes = _meshConverter.Convert(displayMesh);
        if (regionMeshes.Count == 0)
        {
          throw new ConversionException($"Conversion failed for {target}: no meshes generated");
        }
        resultList.AddRange(regionMeshes);
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      // handle other exceptions
      throw new ConversionException($"Conversion failed for {target}: {ex.Message}");
    }

    // return possibly empty list (if FilledRegion generated successfully), because FilledRegion is not a GeometryObject
    return resultList;
  }
}
