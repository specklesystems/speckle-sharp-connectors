using Speckle.Objects.Other;
using Speckle.Sdk.Models.GraphTraversal;
using Speckle.Sdk.Models.Instances;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.Utils.Operations.Receive;

public record RootObjectUnpackerResult(
  IEnumerable<TraversalContext> ObjectsToConvert,
  List<InstanceDefinitionProxy>? DefinitionProxies,
  List<GroupProxy>? GroupProxies,
  List<RenderMaterialProxy>? RenderMaterialProxies,
  List<ColorProxy>? ColorProxies
);
