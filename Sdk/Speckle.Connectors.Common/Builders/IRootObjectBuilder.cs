using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Operations;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.Common.Builders;

public interface IRootObjectBuilder<in T>
{
  public Task<RootObjectBuilderResult> Build(
    IReadOnlyList<T> objects,
    SendInfo sendInfo,
    IProgress<ProgressAction> onOperationProgressed,
    CancellationToken ct = default
  );
}

public record RootObjectBuilderResult(Base RootObject, IEnumerable<SendConversionResult> ConversionResults);
