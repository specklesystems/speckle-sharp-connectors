using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Threading;
using Speckle.Sdk.Models;
using Speckle.Sdk.Serialisation;

namespace Speckle.Connectors.Common.Operations;

public sealed class SendOperation<T>(
  IRootObjectBuilder<T> rootObjectBuilder,
  IRootObjectSender baseObjectSender,
  IThreadContext threadContext
)
{
  public async Task<SendOperationResult> Execute(
    IReadOnlyList<T> objects,
    SendInfo sendInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken ct = default
  )
  {
    var buildResult = await threadContext
      .RunOnMain(() => rootObjectBuilder.Build(objects, sendInfo, onOperationProgressed))
      .ConfigureAwait(false);

    // POC: Jonathon asks on behalf of willow twin - let's explore how this can work
    // buildResult.RootObject["@report"] = new Report { ConversionResults = buildResult.ConversionResults };

    buildResult.RootObject["version"] = 3;
    // base object handler is separated, so we can do some testing on non-production databases
    // exact interface may want to be tweaked when we implement this
    var (rootObjId, convertedReferences) = await threadContext
      .RunOnWorkerAsync(() => baseObjectSender.Send(buildResult.RootObject, sendInfo, onOperationProgressed, ct))
      .ConfigureAwait(false);

    return new(rootObjId, convertedReferences, buildResult.ConversionResults);
  }
}

public record SendOperationResult(
  string RootObjId,
  IReadOnlyDictionary<Id, ObjectReference> ConvertedReferences,
  IEnumerable<SendConversionResult> ConversionResults
);
