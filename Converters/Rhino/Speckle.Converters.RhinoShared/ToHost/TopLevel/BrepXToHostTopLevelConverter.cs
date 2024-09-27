using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
namespace Speckle.Converters.Rhino.ToHost.TopLevel;

[NameAndRankValue(nameof(SOG.BrepX), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class BrepXToHostTopLevelConverter : SpeckleToHostGeometryBaseTopLevelConverter<SOG.BrepX, List<RG.GeometryBase>>
{
  public BrepXToHostTopLevelConverter(
    IConverterSettingsStore<RhinoConversionSettings> settingsStore,
    ITypedConverter<SOG.BrepX, List<RG.GeometryBase>> geometryBaseConverter
  )
    : base(settingsStore, geometryBaseConverter) { }
}

[NameAndRankValue(nameof(SOG.SubDX), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class SubDXToHostTopLevelConverter : SpeckleToHostGeometryBaseTopLevelConverter<SOG.SubDX, List<RG.GeometryBase>>
{
  public SubDXToHostTopLevelConverter(
    IConverterSettingsStore<RhinoConversionSettings> settingsStore,
    ITypedConverter<SOG.SubDX, List<RG.GeometryBase>> geometryBaseConverter
  )
    : base(settingsStore, geometryBaseConverter) { }
}

[NameAndRankValue(nameof(SOG.ExtrusionX), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class ExtrusionXToHostTopLevelConverter : SpeckleToHostGeometryBaseTopLevelConverter<SOG.ExtrusionX, List<RG.GeometryBase>>
{
  public ExtrusionXToHostTopLevelConverter(
    IConverterSettingsStore<RhinoConversionSettings> settingsStore,
    ITypedConverter<SOG.ExtrusionX, List<RG.GeometryBase>> geometryBaseConverter
  )
    : base(settingsStore, geometryBaseConverter) { }
}

