using Speckle.Converters.Common.Objects;

using Speckle.Converters.Common;
using Speckle.Converters.Rhino;


namespace Speckle.Converter.Rhino.ToHost.TopLevel;

[NameAndRankValue(typeof(SA.Text), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class TextObjectToHostTopLevelConverter
    : SpeckleToHostGeometryBaseTopLevelConverter<SA.Text, RG.TextEntity> // Fixed generic type order
{
    public TextObjectToHostTopLevelConverter(
      IConverterSettingsStore<RhinoConversionSettings> settingsStore, // Added required parameter
      ITypedConverter<SA.Text, RG.TextEntity> conversion
    ) : base(settingsStore, conversion) { } // Pass both parameters to base
}
