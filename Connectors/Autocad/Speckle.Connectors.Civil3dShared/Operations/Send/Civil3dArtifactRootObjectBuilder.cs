using Microsoft.Extensions.Logging;
using Speckle.Connectors.Autocad.HostApp;
using Speckle.Connectors.Autocad.Operations.Send;
using Speckle.Connectors.Civil3dShared.HostApp;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Threading;
using Speckle.Converters.Autocad;
using Speckle.Converters.Civil3dShared.ToSpeckle;
using Speckle.Converters.Common;
using Speckle.Objects.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Pipelines.Send.Artifacts;

namespace Speckle.Connectors.Civil3dShared.Operations.Send;

public class Civil3dArtifactRootObjectBuilder : AutocadArtifactRootObjectBuilder
{
  private readonly PropertySetDefinitionHandler _propertySetDefinitionHandler;

  public Civil3dArtifactRootObjectBuilder(
    IRootToSpeckleConverter converter,
    IConverterSettingsStore<AutocadConversionSettings> converterSettings,
    AutocadInstanceUnpacker instanceUnpacker,
    AutocadMaterialUnpacker materialUnpacker,
    AutocadColorUnpacker colorUnpacker,
    IThreadContext threadContext,
    IArtifactPipelineFactory artifactPipelineFactory,
    ISpeckleApplication speckleApplication,
    ILogger<AutocadArtifactRootObjectBuilder> logger,
    PropertySetDefinitionHandler propertySetDefinitionHandler
  )
    : base(
      converter,
      converterSettings,
      instanceUnpacker,
      materialUnpacker,
      colorUnpacker,
      threadContext,
      artifactPipelineFactory,
      speckleApplication,
      logger
    )
  {
    _propertySetDefinitionHandler = propertySetDefinitionHandler;
  }

  protected override void EmitAdditionalNodes(ObjectsArtifactPipeline pipeline)
  {
    if (_propertySetDefinitionHandler.Definitions.Count == 0)
    {
      return;
    }

    var definitions = new Dictionary<string, object?>();
    foreach (var kvp in _propertySetDefinitionHandler.Definitions)
    {
      definitions[kvp.Key] = kvp.Value;
    }

    var properties = new Dictionary<string, object?> { [ProxyKeys.PROPERTYSET_DEFINITIONS] = definitions };
    pipeline.InternObject(PropertySetBaker.DEFINITIONS_CARRIER_APP_ID);
    pipeline.AddProperties(PropertySetBaker.DEFINITIONS_CARRIER_APP_ID, properties);
  }
}
