using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.RevitShared.ToHost.TopLevel;

public abstract class CurveToDirectShapeConverterToHostBase<TCurve>
  : ITypedConverter<TCurve, List<DB.GeometryObject>>,
    IToHostTopLevelConverter
  where TCurve : Base, ICurve
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly ITypedConverter<ICurve, DB.CurveArray> _curveConverter;

  protected CurveToDirectShapeConverterToHostBase(
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    ITypedConverter<ICurve, DB.CurveArray> curveConverter
  )
  {
    _converterSettings = converterSettings;
    _curveConverter = curveConverter;
  }

  public List<DB.GeometryObject> Convert(TCurve target)
  {
    var converted = new List<DB.GeometryObject>();

    DB.CurveArray curveArray = _curveConverter.Convert(target);
    converted.AddRange(curveArray.Cast<DB.Curve>());

    var genericModelCategory = _converterSettings.Current.Document.Settings.Categories.get_Item(
      DB.BuiltInCategory.OST_GenericModel
    );

    using var revitDs = DB.DirectShape.CreateElement(_converterSettings.Current.Document, genericModelCategory.Id);
    if (target is Base speckleObject && speckleObject.applicationId != null)
    {
      revitDs.ApplicationId = speckleObject.applicationId;
    }

    revitDs.ApplicationDataId = Guid.NewGuid().ToString();
    revitDs.SetShape(converted);
    revitDs.Name = "CurveAsDirectShape";

    return converted;
  }

  public object Convert(Base target) => Convert((TCurve)target);
}
