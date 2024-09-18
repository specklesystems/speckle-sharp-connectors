using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Converters.RevitShared.ToSpeckle;

namespace Speckle.Converters.RevitShared.ToHost.ToLevel;

[NameAndRankValue(nameof(SOBE.Level), 0)]
public class LevelToHostTopLevelConverter : BaseTopLevelConverterToHost<SOBE.Level, DB.Level>
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly ScalingServiceToHost _scalingService;

  public LevelToHostTopLevelConverter(
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    ScalingServiceToHost scalingService
  )
  {
    _converterSettings = converterSettings;
    _scalingService = scalingService;
  }

  public override DB.Level Convert(SOBE.Level target)
  {
    using var documentLevelCollector = new DB.FilteredElementCollector(_converterSettings.Current.Document);
    var docLevels = documentLevelCollector.OfClass(typeof(DB.Level)).ToElements().Cast<DB.Level>();

    // POC : I'm not really understanding the linked use case for this. Do we want to bring this over?

    //bool elevationMatch = true;
    ////level by name component
    //if (target is RevitLevel speckleRevitLevel && speckleRevitLevel.referenceOnly)
    //{
    //  //see: https://speckle.community/t/revit-connector-levels-and-spaces/2824/5
    //  elevationMatch = false;
    //  if (GetExistingLevelByName(docLevels, target.name) is DB.Level existingLevelWithSameName)
    //  {
    //    return existingLevelWithSameName;
    //  }
    //}

    DB.Level revitLevel;
    var targetElevation = _scalingService.ScaleToNative(target.elevation, target.units);

    if (GetExistingLevelByElevation(docLevels, targetElevation) is DB.Level existingLevel)
    {
      revitLevel = existingLevel;
    }
    else
    {
      revitLevel = DB.Level.Create(_converterSettings.Current.Document, targetElevation);
      revitLevel.Name = target.name;

      if (target is SOBR.RevitLevel rl && rl.createView)
      {
        using var viewPlan = CreateViewPlan(target.name, revitLevel.Id);
      }
    }

    return revitLevel;
  }

  private DB.Level GetExistingLevelByElevation(IEnumerable<DB.Level> docLevels, double elevation) =>
    docLevels.First(l => Math.Abs(l.Elevation - elevation) < _converterSettings.Current.Tolerance);

  private DB.ViewPlan CreateViewPlan(string name, DB.ElementId levelId)
  {
    using var collector = new DB.FilteredElementCollector(_converterSettings.Current.Document);
    var vt = collector
      .OfClass(typeof(DB.ViewFamilyType))
      .First(el => ((DB.ViewFamilyType)el).ViewFamily == DB.ViewFamily.FloorPlan);

    var view = DB.ViewPlan.Create(_converterSettings.Current.Document, vt.Id, levelId);
    view.Name = name;

    return view;
  }
}