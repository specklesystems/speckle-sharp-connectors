using Speckle.Converters.Common;
using Speckle.InterfaceGenerator;

namespace Speckle.Converters.Plant3dShared;

[GenerateAutoInterface]
public class Plant3dConversionSettingsFactory(IHostToSpeckleUnitConverter<AAEC.BuiltInUnit> unitsConverter)
  : IPlant3dConversionSettingsFactory
{
  public Plant3dConversionSettings Create(Document document) =>
    new(document, unitsConverter.ConvertOrThrow(GetDocBuiltInUnit(document)));

  // Plant3D uses the same AEC unit system as Civil3D
  private static AAEC.BuiltInUnit GetDocBuiltInUnit(Document doc)
  {
    AAEC.BuiltInUnit unit = AAEC.BuiltInUnit.Dimensionless;
    using (ADB.Transaction tr = doc.Database.TransactionManager.StartTransaction())
    {
      Autodesk.AutoCAD.DatabaseServices.ObjectId id = AAEC.ApplicationServices.DrawingSetupVariables.GetInstance(
        doc.Database,
        false
      );
      if (tr.GetObject(id, ADB.OpenMode.ForRead) is AAEC.ApplicationServices.DrawingSetupVariables setupVariables)
      {
        unit = setupVariables.LinearUnit;
      }
      tr.Commit();
    }
    return unit;
  }
}
