using Speckle.Converters.Common;
using Speckle.InterfaceGenerator;

namespace Speckle.Converters.Civil3d;

public class Civil3dConversionSettings : IConverterSettings
{
  public Document Document { get; init; }
  public string SpeckleUnits { get; init; }
}

[GenerateAutoInterface]
public class Civil3dConversionSettingsFactory(IHostToSpeckleUnitConverter<AAEC.BuiltInUnit> unitsConverter)
  : ICivil3dConversionSettingsFactory
{
  public Civil3dConversionSettings Create(Document document) =>
    new() { Document = document, SpeckleUnits = unitsConverter.ConvertOrThrow(GetDocBuiltInUnit(document)) };

  private static AAEC.BuiltInUnit GetDocBuiltInUnit(Document doc)
  {
    AAEC.BuiltInUnit unit = AAEC.BuiltInUnit.Dimensionless;

    using (ADB.Transaction tr = doc.Database.TransactionManager.StartTransaction())
    {
      ADB.ObjectId id = AAEC.ApplicationServices.DrawingSetupVariables.GetInstance(doc.Database, false);
      if (tr.GetObject(id, ADB.OpenMode.ForRead) is AAEC.ApplicationServices.DrawingSetupVariables setupVariables)
      {
        unit = setupVariables.LinearUnit;
      }

      tr.Commit();
    }

    return unit;
  }
}
