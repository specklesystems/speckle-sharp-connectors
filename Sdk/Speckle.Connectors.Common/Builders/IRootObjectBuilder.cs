using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Operations;
using Speckle.Sdk.Models;
using Speckle.Sdk.Pipelines.Send;

namespace Speckle.Connectors.Common.Builders;

public interface IRootObjectBuilder<in T>
{
  public Task<RootObjectBuilderResult> Build(
    IReadOnlyList<T> objects,
    string projectId,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  );
}

public interface IRootContinuousTraversalBuilder<in T>
{
  public Task<RootObjectBuilderResult> Build(
    IReadOnlyList<T> objects,
    string projectId,
    SendPipeline sendPipeline,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  );
}

public record RootObjectBuilderResult(Base RootObject, IReadOnlyList<SendConversionResult> ConversionResults);
