using Autodesk.AutoCAD.DatabaseServices;
using Speckle.Converters.Common;
using Speckle.InterfaceGenerator;

namespace Speckle.Converters.Plant3dShared;

[GenerateAutoInterface]
public class Plant3dConversionSettingsFactory(IHostToSpeckleUnitConverter<UnitsValue> unitsConverter)
  : IPlant3dConversionSettingsFactory
{
  public Plant3dConversionSettings Create(Document document) =>
    new(document, unitsConverter.ConvertOrThrow(document.Database.Insunits));
}
