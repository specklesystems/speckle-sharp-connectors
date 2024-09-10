using Speckle.Connectors.Utils.Conversion;
using Speckle.Connectors.Utils.Operations;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.Utils.Builders;

public interface IRootObjectBuilder<in T>
{
  public Task<RootObjectBuilderResult> Build(
    IReadOnlyList<T> objects,
    SendInfo sendInfo,
    Action<string, double?>? onOperationProgressed = null,
    CancellationToken ct = default
  );
}

public record RootObjectBuilderResult(Base RootObject, IEnumerable<SendConversionResult> ConversionResults);
