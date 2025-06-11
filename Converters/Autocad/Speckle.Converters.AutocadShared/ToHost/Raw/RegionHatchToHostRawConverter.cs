using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.ToHost.Raw;

public class RegionHatchToHostRawConverter : ITypedConverter<SOG.Region, ADB.Hatch>
{
  private readonly ITypedConverter<ICurve, List<(ADB.Entity, Base)>> _curveConverter;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public RegionHatchToHostRawConverter(
    ITypedConverter<ICurve, List<(ADB.Entity, Base)>> curveConverter,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  )
  {
    _curveConverter = curveConverter;
    _settingsStore = settingsStore;
  }

  public ADB.Hatch Convert(SOG.Region target)
  {
    // Access a top-level transaction
    ADB.Transaction tr = _settingsStore.Current.Document.TransactionManager.TopTransaction;
    var btr = (ADB.BlockTableRecord)
      tr.GetObject(_settingsStore.Current.Document.Database.CurrentSpaceId, ADB.OpenMode.ForWrite);

    // initialize Hatch, append to blockTableRecord
    ADB.Hatch acHatch = new();
    btr.AppendEntity(acHatch);
    tr.AddNewlyCreatedDBObject(acHatch, true);

    // Set essential properties of the hatch object
    acHatch.SetDatabaseDefaults();
    acHatch.SetHatchPattern(ADB.HatchPatternType.PreDefined, "SOLID");

    // Associative property must be set after the hatch object is
    // appended to the block table record and before AppendLoop
    acHatch.Associative = true;

    // convert and assign boundary loop
    ConvertAndAssignHatchLoop(btr, tr, acHatch, target.boundary, ADB.HatchLoopTypes.External);
    foreach (var loop in target.innerLoops)
    {
      ConvertAndAssignHatchLoop(btr, tr, acHatch, loop, ADB.HatchLoopTypes.Outermost);
    }

    return acHatch;
  }

  private void ConvertAndAssignHatchLoop(
    ADB.BlockTableRecord acBlkTblRec,
    ADB.Transaction acTrans,
    ADB.Hatch hatch,
    ICurve curve,
    ADB.HatchLoopTypes loopType
  )
  {
    // convert loop, add to ObjectIdCollection
    var convertedCurve = _curveConverter.Convert(curve);
    CheckForNonPlanarLoops(convertedCurve);
    var dbCurve = (ADB.Curve)convertedCurve[0].Item1;

    // If Spline, turn into segmented polyline - this is how AutoCAD best imports Hatches with Curve boundaries from Rhino
    // Splines from AutoCAD don't need to be segmented (shape is fully preserved), but we don't have a way to distinguish them
    if (dbCurve is ADB.Spline spline)
    {
      if (spline.NurbsData.Degree == 1)
      {
        // for simple polylines ".ToPolylineWithPrecision" distorts the shape, so just applying a list of vertices
        dbCurve = new ADB.Polyline3d(ADB.Poly3dType.SimplePoly, spline.NurbsData.GetControlPoints(), true);
      }
      else
      {
        dbCurve = spline.ToPolylineWithPrecision(10, false, false);
      }
    }
    using ADB.ObjectIdCollection tempDBObjColl = CreateTempObjectIdCollection(acBlkTblRec, acTrans, dbCurve);

    // append loop: possible Autodesk.AutoCAD.Runtime.Exception: eInvalidInput
    hatch.AppendLoop(loopType, tempDBObjColl);
    hatch.EvaluateHatch(true);
    dbCurve.Erase();
  }

  private ADB.ObjectIdCollection CreateTempObjectIdCollection(
    ADB.BlockTableRecord acBlkTblRec,
    ADB.Transaction acTrans,
    ADB.Entity loopEntity
  )
  {
    // Add the new curve object to the block table record and the transaction
    acBlkTblRec.AppendEntity(loopEntity);
    acTrans.AddNewlyCreatedDBObject(loopEntity, true);

    // Adds the entity to an object id array
    ADB.ObjectIdCollection tempDBObjColl = new();
    tempDBObjColl.Add(loopEntity.ObjectId);

    return tempDBObjColl;
  }

  private void CheckForNonPlanarLoops(List<(ADB.Entity, Base)> convertedResult)
  {
    if (convertedResult.Count != 1)
    {
      // this will only be the case if it was a non-planar Polycurve: throw error
      throw new ConversionException($"Non-planar Polycurve cannot be used as a Region loop: {convertedResult}");
    }
  }
}
