using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Operations;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.Common.Builders;

public interface IRootObjectBuilder<in TInput>
{
  public Task<RootObjectBuilderResult> Build(
    TInput input,
    SendInfo sendInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken ct = default
  );
}

public record RootObjectBuilderResult(Base RootObject, IEnumerable<SendConversionResult> ConversionResults);
