using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;
using SOG = Speckle.Objects.Geometry;

namespace Speckle.Converter.Tekla2024.ToSpeckle.TopLevel;

[NameAndRankValue(nameof(TSM.ModelObject), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class ModelObjectToSpeckleConverter : IToSpeckleTopLevelConverter
{
    private readonly IConverterSettingsStore<TeklaConversionSettings> _settingsStore;
    private readonly ITypedConverter<TSM.Beam, Base> _beamConverter;
    private readonly ITypedConverter<TSM.Solid, SOG.Mesh> _meshConverter;

    public ModelObjectToSpeckleConverter(
        IConverterSettingsStore<TeklaConversionSettings> settingsStore,
        ITypedConverter<TSM.Beam, Base> beamConverter,
        ITypedConverter<TSM.Solid, SOG.Mesh> meshConverter)
    {
        _settingsStore = settingsStore;
        _beamConverter = beamConverter;
        _meshConverter = meshConverter;
    }

    public Base Convert(object target)
    {
        if (target is not TSM.ModelObject modelObject)
        {
            throw new ArgumentException($"Target object is not a ModelObject. It's a {target.GetType()}");
        }

        switch (modelObject)
        {
            case TSM.Beam beam:
                return _beamConverter.Convert(beam);
            default:
                // handle any ModelObject with basic properties
                var baseObject = new Base
                {
                    ["type"] = modelObject.GetType().ToString().Split('.').Last(),
                    ["units"] = _settingsStore.Current.SpeckleUnits,
                    applicationId = modelObject.Identifier.GUID.ToString(),
                };

                return baseObject;
        }
    }
}
