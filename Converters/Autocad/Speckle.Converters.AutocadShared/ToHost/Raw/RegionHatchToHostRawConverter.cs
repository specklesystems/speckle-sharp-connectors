using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.Autocad.ToHost.Raw;

public class RegionHatchToHostRawConverter : ITypedConverter<SOG.Region, ADB.Hatch>
{
  private readonly ITypedConverter<ICurve, ADB.Curve> _curveConverter;
  private readonly ITypedConverter<SOG.Region, ADB.Region> _regionConverter;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public RegionHatchToHostRawConverter(
    ITypedConverter<ICurve, ADB.Curve> curveConverter,
    ITypedConverter<SOG.Region, ADB.Region> regionConverter,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  )
  {
    _curveConverter = curveConverter;
    _regionConverter = regionConverter;
    _settingsStore = settingsStore;
  }

  public ADB.Hatch Convert(SOG.Region target)
  {
    // Add converted loops to the list if segmentCollections
    /*
    var loopsCurves = new List<ADB.Curve>();
    foreach (var loop in target.innerLoops)
    {
      loopsCurves.Add(_curveConverter.Convert(loop));
    }
    */

    // Get the current document and database
    Document acDoc = _settingsStore.Current.Document;
    ADB.Database acCurDb = acDoc.Database;

    // Start a transaction
    //using (ADB.Transaction acTrans = acCurDb.TransactionManager.StartTransaction())
    //{
    ADB.Transaction acTrans = acCurDb.TransactionManager.StartTransaction();
    // Open the Block table for read
    ADB.BlockTable? acBlkTbl = acTrans.GetObject(acCurDb.BlockTableId, ADB.OpenMode.ForRead) as ADB.BlockTable;
    if (acBlkTbl == null)
    {
      throw new ConversionException();
    }
    // Open the Block table record Model space for write
    ADB.BlockTableRecord? acBlkTblRec =
      acTrans.GetObject(acBlkTbl[ADB.BlockTableRecord.ModelSpace], ADB.OpenMode.ForWrite) as ADB.BlockTableRecord;
    if (acBlkTblRec == null)
    {
      throw new ConversionException();
    }

    // Add the new curve object to the block table record and the transaction
    ADB.Entity boundaryEntity = _curveConverter.Convert(target.boundary);
    acBlkTblRec.AppendEntity(boundaryEntity);
    acTrans.AddNewlyCreatedDBObject(boundaryEntity, true);

    // Adds the circle to an object id array
    ADB.ObjectIdCollection boundaryDBObjColl = new();
    boundaryDBObjColl.Add(boundaryEntity.ObjectId);

    //using ()
    //{
    ADB.Hatch acHatch = new();
    acBlkTblRec.AppendEntity(acHatch);
    acTrans.AddNewlyCreatedDBObject(acHatch, true);

    // Set the properties of the hatch object: Associative must be set after the hatch object is
    // appended to the block table record and before AppendLoop
    acHatch.SetHatchPattern(ADB.HatchPatternType.PreDefined, "ANSI31");
    acHatch.Associative = true;
    acHatch.AppendLoop(ADB.HatchLoopTypes.Outermost, boundaryDBObjColl);
    acHatch.EvaluateHatch(true);

    // Save the new object to the database
    acTrans.Commit();

    return acHatch;
    //}
    //}
  }
}
