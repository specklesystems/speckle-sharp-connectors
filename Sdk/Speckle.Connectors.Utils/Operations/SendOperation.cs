using Speckle.Connectors.Utils.Builders;
using Speckle.Connectors.Utils.Conversion;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.Utils.Operations;

public sealed class SendOperation<T>
{
  private readonly IRootObjectBuilder<T> _rootObjectBuilder;
  private readonly IRootObjectSender _baseObjectSender;

  public SendOperation(IRootObjectBuilder<T> rootObjectBuilder, IRootObjectSender baseObjectSender)
  {
    _rootObjectBuilder = rootObjectBuilder;
    _baseObjectSender = baseObjectSender;
  }

  public async Task<SendOperationResult> Execute(
    IReadOnlyList<T> objects,
    SendInfo sendInfo,
    Action<string, double?>? onOperationProgressed = null,
    CancellationToken ct = default
  )
  {
    using var activity = SpeckleActivityFactory.Start("SendOperation");
    var buildResult = _rootObjectBuilder.Build(objects, sendInfo, onOperationProgressed, ct);

    // POC: Jonathon asks on behalf of willow twin - let's explore how this can work
    // buildResult.RootObject["@report"] = new Report { ConversionResults = buildResult.ConversionResults };

    // base object handler is separated, so we can do some testing on non-production databases
    // exact interface may want to be tweaked when we implement this
    var (rootObjId, convertedReferences) = await _baseObjectSender
      .Send(buildResult.RootObject, sendInfo, onOperationProgressed, ct)
      .ConfigureAwait(false);

    return new(rootObjId, convertedReferences, buildResult.ConversionResults);
  }
}

public record SendOperationResult(
  string RootObjId,
  IReadOnlyDictionary<string, ObjectReference> ConvertedReferences,
  IEnumerable<SendConversionResult> ConversionResults
);
