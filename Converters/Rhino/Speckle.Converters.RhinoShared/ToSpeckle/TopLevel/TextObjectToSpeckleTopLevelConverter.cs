using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Rhino.ToSpeckle.TopLevel; // <-- Fixed namespace

[NameAndRankValue(typeof(SA.Text), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class TextObjectToSpeckleTopLevelConverter : SpeckleToHostGeometryBaseTopLevelConverter<SA.Text, RG.TextEntity> // <-- Fixed class name
{
    public TextObjectToSpeckleTopLevelConverter(
      IConverterSettingsStore<RhinoConversionSettings> settingsStore,
      ITypedConverter<SA.Text, RG.TextEntity> textConverter
    )
      : base(settingsStore, textConverter) { }
}
