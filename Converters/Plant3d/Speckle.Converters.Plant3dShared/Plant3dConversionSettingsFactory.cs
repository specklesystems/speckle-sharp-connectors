using Speckle.Converters.Common;
using Speckle.InterfaceGenerator;

namespace Speckle.Converters.Plant3dShared;

[GenerateAutoInterface]
public class Plant3dConversionSettingsFactory(IHostToSpeckleUnitConverter<AAEC.BuiltInUnit> unitsConverter)
  : IPlant3dConversionSettingsFactory
{
  public Plant3dConversionSettings Create(Document document) =>
    new(document, unitsConverter.ConvertOrThrow(GetDocBuiltInUnit(document)));

  // Plant3D may or may not have AEC drawing setup variables.
  // Falls back to AutoCAD INSUNITS if AEC variables aren't available.
  private static AAEC.BuiltInUnit GetDocBuiltInUnit(Document doc)
  {
    AAEC.BuiltInUnit unit = AAEC.BuiltInUnit.Dimensionless;
    using (ADB.Transaction tr = doc.Database.TransactionManager.StartTransaction())
    {
      Autodesk.AutoCAD.DatabaseServices.ObjectId id = AAEC.ApplicationServices.DrawingSetupVariables.GetInstance(
        doc.Database,
        false
      );
      if (!id.IsNull
        && tr.GetObject(id, ADB.OpenMode.ForRead) is AAEC.ApplicationServices.DrawingSetupVariables setupVariables)
      {
        unit = setupVariables.LinearUnit;
      }
      tr.Commit();
    }

    // If AEC variables aren't set, fall back to AutoCAD INSUNITS
    if (unit == AAEC.BuiltInUnit.Dimensionless)
    {
      unit = doc.Database.Insunits switch
      {
        ADB.UnitsValue.Millimeters => AAEC.BuiltInUnit.Millimeter,
        ADB.UnitsValue.Centimeters => AAEC.BuiltInUnit.Centimeter,
        ADB.UnitsValue.Meters => AAEC.BuiltInUnit.Meter,
        ADB.UnitsValue.Inches => AAEC.BuiltInUnit.Inch,
        ADB.UnitsValue.Feet => AAEC.BuiltInUnit.Foot,
        _ => AAEC.BuiltInUnit.Dimensionless,
      };
    }
    return unit;
  }
}
