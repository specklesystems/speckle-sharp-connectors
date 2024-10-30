using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converter.Tekla2024.ToSpeckle.TopLevel;

[NameAndRankValue(nameof(TSM.ModelObject), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class ModelObjectToSpeckleConverter : IToSpeckleTopLevelConverter
{
    private readonly IConverterSettingsStore<TeklaConversionSettings> _settingsStore;
    private readonly ITypedConverter<TSM.Beam, Base> _beamConverter;
    private readonly ITypedConverter<TSM.ContourPlate, Base> _plateConverter;
    public ModelObjectToSpeckleConverter(
        IConverterSettingsStore<TeklaConversionSettings> settingsStore,
        ITypedConverter<TSM.Beam, Base> beamConverter,
        ITypedConverter<TSM.ContourPlate, Base> plateConverter)
    {
        _settingsStore = settingsStore;
        _beamConverter = beamConverter;
        _plateConverter = plateConverter;
    }

    public Base Convert(object target)
    {
        if (target is not TSM.ModelObject modelObject)
        {
            throw new ArgumentException($"Target object is not a ModelObject. It's a {target.GetType()}");
        }

        Base result;
        switch (modelObject)
        {
            case TSM.Beam beam:
                result =  _beamConverter.Convert(beam);
                break;
            case TSM.ContourPlate plate:
              result = _plateConverter.Convert(plate);
              break;
            default:
              throw new ConversionNotSupportedException(
                $"Conversion of {target.GetType().Name} to Speckle is not supported.");
        }
        
        result["type"] = modelObject.GetType().ToString().Split('.').Last();
        result["units"] = _settingsStore.Current.SpeckleUnits;
        
        return result;
    }
}
