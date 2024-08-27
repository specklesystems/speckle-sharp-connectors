using Autodesk.Revit.DB;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Converters.RevitShared.Helpers;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
  "Naming",
  "CA1711:Identifiers should not have incorrect suffix",
  Justification = "See base class justification"
)]
// POC: so this should *probably* be Document and NOT UI.UIDocument, the former is Conversion centric
// and the latter is more for connector
public class RevitConversionContextStack : ConversionContextStack<Document, ForgeTypeId>, IRevitConversionContextStack
{
  public ToSpeckleSettings ToSpeckleSettings { get; }

  /// <summary>
  /// Persistent cache (across conversions) for all generated render material proxies. Note this cache stores a list of render material proxies per element id.
  /// </summary>
  public RevitMaterialCacheSingleton RenderMaterialProxyCache { get; }

  public const double TOLERANCE = 0.0164042; // 5mm in ft

  public RevitConversionContextStack(
    RevitContext context,
    IHostToSpeckleUnitConverter<ForgeTypeId> unitConverter,
    RevitMaterialCacheSingleton renderMaterialProxyCache,
    ToSpeckleSettings toSpeckleSettings
  )
    : base(
      // POC: we probably should not get here without a valid document
      // so should this perpetuate or do we assume this is valid?
      // relting on the context.UIApplication?.ActiveUIDocument is not right
      // this should be some IActiveDocument I suspect?
      context.UIApplication?.ActiveUIDocument?.Document
        ?? throw new SpeckleConversionException("Active UI document could not be determined"),
      context.UIApplication.ActiveUIDocument.Document.GetUnits().GetFormatOptions(SpecTypeId.Length).GetUnitTypeId(),
      unitConverter
    )
  {
    ToSpeckleSettings = toSpeckleSettings;
    RenderMaterialProxyCache = renderMaterialProxyCache;
  }
}
