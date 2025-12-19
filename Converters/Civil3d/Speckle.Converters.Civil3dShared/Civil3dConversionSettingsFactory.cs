using Speckle.Converters.Common;
using Speckle.InterfaceGenerator;

namespace Speckle.Converters.Civil3dShared;

[GenerateAutoInterface]
public class Civil3dConversionSettingsFactory(IHostToSpeckleUnitConverter<AAEC.BuiltInUnit> unitsConverter)
  : ICivil3dConversionSettingsFactory
{
  public Civil3dConversionSettings Create(Document document, bool mappingToRevitCategories = false) =>
    new(document, unitsConverter.ConvertOrThrow(GetDocBuiltInUnit(document)), mappingToRevitCategories);

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
