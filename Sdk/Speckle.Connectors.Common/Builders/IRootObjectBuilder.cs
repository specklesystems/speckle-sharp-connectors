using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Operations;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.Common.Builders;

public interface IRootObjectBuilder<in T>
{
  public RootObjectBuilderResult Build(
    IReadOnlyList<T> objects,
    SendInfo sendInfo,
    IProgress<CardProgress> onOperationProgressed
  );
}

public record RootObjectBuilderResult(Base RootObject, IEnumerable<SendConversionResult> ConversionResults);
