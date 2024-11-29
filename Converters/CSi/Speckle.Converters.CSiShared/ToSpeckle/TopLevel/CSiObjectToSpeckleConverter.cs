using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
// using Speckle.Objects.Geometry;
// This will come at a later stage: using Speckle.Objects.Data;
using Speckle.Sdk.Models;

namespace Speckle.Converters.CSiShared.ToSpeckle.TopLevel;

[NameAndRankValue(nameof(CSiPointWrapper), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class CSiPointToSpeckleConverter : CSiObjectToSpeckleConverter<CSiPointWrapper, CSiObject>
{
  public CSiPointToSpeckleConverter(
    IConverterSettingsStore<CSiConversionSettings> settingsStore,
    ITypedConverter<CSiPointWrapper, CSiObject> converter
  )
    : base(settingsStore, converter) { }
}

public abstract class CSiObjectToSpeckleConverter<TInput, TOutput> : IToSpeckleTopLevelConverter
  where TInput : ICSiWrapper
  where TOutput : Base
{
  private readonly IConverterSettingsStore<CSiConversionSettings> _settingsStore;
  private readonly ITypedConverter<TInput, TOutput> _converter;

  protected CSiObjectToSpeckleConverter(
    IConverterSettingsStore<CSiConversionSettings> settingsStore,
    ITypedConverter<TInput, TOutput> converter
  )
  {
    _settingsStore = settingsStore;
    _converter = converter;
  }

  public Base Convert(object target)
  {
    if (target is not TInput csiWrapper)
    {
      throw new ArgumentException($"Target object is not a CSi object. It's a {target.GetType()}");
    }

    var point = _converter.Convert(csiWrapper);

    var result = new CSiObject // This should be coming from sdk
    {
      type = target.GetType().ToString().Split('.').Last(),
      units = _settingsStore.Current.SpeckleUnits,
      name = csiWrapper.Name,
      displayValue = new List<Base> { point }
    };

    // Get properties (material, section, etc.)
    // _propertyExtractor, ObjectPropertyExtractor or similar in a Helpers folder?

    // Get display value (geometry)
    // _displayValueExtractor DisplayValueExtractor or SpatialDataExtractor or GeometricDataExtractor in a Helpers folder

    return result;
  }
}
