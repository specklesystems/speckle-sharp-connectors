using Speckle.Connectors.Autocad.HostApp;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Converters.Common;

namespace Speckle.Connectors.Autocad.Operations.Receive;

/// <summary>
/// <para>AutoCAD-specific host object builder. Expects to be a scoped dependency per receive operation.</para>
/// </summary>
public sealed class AutocadHostObjectBuilder : AutocadHostObjectBaseBuilder
{
  public AutocadHostObjectBuilder(
    IRootToHostConverter converter,
    AutocadLayerBaker layerBaker,
    AutocadGroupBaker groupBaker,
    AutocadInstanceBaker instanceBaker,
    IAutocadMaterialBaker materialBaker,
    IAutocadColorBaker colorBaker,
    AutocadContext autocadContext,
    RootObjectUnpacker rootObjectUnpacker,
    IReceiveConversionHandler conversionHandler
  )
    : base(
      converter,
      layerBaker,
      groupBaker,
      instanceBaker,
      materialBaker,
      colorBaker,
      autocadContext,
      rootObjectUnpacker,
      conversionHandler
    ) { }
}
