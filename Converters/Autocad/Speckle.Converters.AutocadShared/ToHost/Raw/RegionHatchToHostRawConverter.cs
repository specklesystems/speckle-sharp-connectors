using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Sdk;
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
    // Get the current document and database
    Document acDoc = _settingsStore.Current.Document;
    ADB.Database acCurDb = acDoc.Database;

    // Start a transaction
    ADB.Transaction tr = acCurDb.TransactionManager.StartTransaction();
    var btr = (ADB.BlockTableRecord)
      tr.GetObject(_settingsStore.Current.Document.Database.CurrentSpaceId, ADB.OpenMode.ForWrite);

    // initialize Hatch, only once, with the boundary
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
    // catch any exception to finish the transaction, throw it later
    Exception? exception = null;
    try
    {
      ConvertAndAssignHatchLoop(btr, tr, acHatch, target.boundary, ADB.HatchLoopTypes.External);
      foreach (var loop in target.innerLoops)
      {
        ConvertAndAssignHatchLoop(btr, tr, acHatch, loop, ADB.HatchLoopTypes.Outermost);
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      exception = ex;
    }

    // Save the new object to the database
    tr.Commit();

    // throw any possible exception after finishing transaction
    if (exception != null)
    {
      throw exception;
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

    // If Spline, turn into segmented polyline - this is how AutoCAD imports Hatches with Curve boundaries from Rhino
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
