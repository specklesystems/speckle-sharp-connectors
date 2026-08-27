using Speckle.Sdk.Pipelines.Progress;
using Speckle.Sdk.Pipelines.Receive.Artifacts;

namespace Speckle.Connectors.Common.Builders;

/// <summary>
/// Which model is being received, by both name and id. Names are what the host document shows the user (layer/group
/// names); ids are stable across a rename on the web, so a host that tracks a prior bake in order to clean it up
/// should key on those [ENG-8805].
/// </summary>
public readonly record struct ArtefactReceiveTarget(
  string ProjectId,
  string ProjectName,
  string ModelId,
  string ModelName
);

/// <summary>
/// Host builder for the Speckle 4.0 artefact path that bakes a parsed <see cref="ArtefactBundle"/> <b>directly</b> into
/// the host application — geometry straight from the parquet blobs (3dm solids / SGEO meshes), layers/materials/instances
/// straight from the envelope graph — without reconstructing the v1 <c>Base</c>/<c>DataObject</c> graph or going through
/// the v1 traversal + converter pipeline. The receive-side twin of the send-side <c>IArtifactRootObjectBuilder</c>.
/// Connectors that can consume the artefact geometry natively (Rhino: 3dm) register this; connectors that cannot
/// leave it unregistered and fall back to the <c>ObjectsArtifactReader</c> reconstruction + the v1 host builder.
/// </summary>
public interface IArtifactHostObjectBuilder
{
  Task<HostObjectBuilderResult> Build(
    ArtefactBundle bundle,
    ArtefactReceiveTarget target,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  );
}
