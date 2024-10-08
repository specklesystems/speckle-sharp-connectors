using Speckle.Converters.Common;
using Speckle.InterfaceGenerator;

namespace Speckle.Converters.Civil3d;


[GenerateAutoInterface]
public class Civil3dConversionSettingsFactory(IHostToSpeckleUnitConverter<Autodesk.Aec.BuiltInUnit> unitsConverter)
  : ICivil3dConversionSettingsFactory
{
  public Civil3dConversionSettings Create(Document document) =>
    new(document, unitsConverter.ConvertOrThrow(GetDocBuiltInUnit(document)));
  
  private static Autodesk.Aec.BuiltInUnit GetDocBuiltInUnit(Document doc)
  {
    Autodesk.Aec.BuiltInUnit unit = Autodesk.Aec.BuiltInUnit.Dimensionless;
    using ( Autodesk.AutoCAD.DatabaseServices.Transaction tr = doc.Database.TransactionManager.StartTransaction())
    {
      Autodesk.AutoCAD.DatabaseServices.ObjectId id = Autodesk.Aec.ApplicationServices.DrawingSetupVariables.GetInstance(doc.Database, false);
      if (tr.GetObject(id,  Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead) is Autodesk.Aec.ApplicationServices.DrawingSetupVariables setupVariables)
      {
        unit = setupVariables.LinearUnit;
      }
      tr.Commit();
    }
    return unit;
  }
}
