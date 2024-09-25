using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Conversion;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.Common.Operations;

public sealed class SendOperation<T>
{
  private readonly IRootObjectBuilder<T> _rootObjectBuilder;
  private readonly IRootObjectSender _baseObjectSender;
  private readonly ISdkActivityFactory _activityFactory;

  public SendOperation(
    IRootObjectBuilder<T> rootObjectBuilder,
    IRootObjectSender baseObjectSender,
    ISdkActivityFactory activityFactory
  )
  {
    _rootObjectBuilder = rootObjectBuilder;
    _baseObjectSender = baseObjectSender;
    _activityFactory = activityFactory;
  }

  public async Task<SendOperationResult> Execute(
    IReadOnlyList<T> objects,
    SendInfo sendInfo,
    Action<string, double?>? onOperationProgressed = null,
    CancellationToken ct = default
  )
  {
    using var activity = _activityFactory.Start("SendOperation");
    var buildResult = await _rootObjectBuilder
      .Build(objects, sendInfo, onOperationProgressed, ct)
      .ConfigureAwait(false);

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
