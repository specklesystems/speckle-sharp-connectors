using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Operations;
using Speckle.Sdk.Models;

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

public record RootObjectBuilderResult(Base RootObject, IReadOnlyList<SendConversionResult> ConversionResults);
