using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Rhino;

namespace Speckle.Converter.Rhino.ToHost.TopLevel;

[NameAndRankValue(typeof(SA.Text), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class TextObjectToHostTopLevelConverter : SpeckleToHostGeometryBaseTopLevelConverter<SA.Text, RG.TextEntity>
{
  public TextObjectToHostTopLevelConverter(
    IConverterSettingsStore<RhinoConversionSettings> settingsStore,
    ITypedConverter<SA.Text, RG.TextEntity> conversion
  )
    : base(settingsStore, conversion) { }
}
