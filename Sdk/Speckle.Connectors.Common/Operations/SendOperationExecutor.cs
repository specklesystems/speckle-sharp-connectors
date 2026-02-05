using Speckle.InterfaceGenerator;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Serialisation.V2;
using Speckle.Sdk.Serialisation.V2.Send;
using Speckle.Sdk.Transports;

namespace Speckle.Connectors.Common.Operations;

[GenerateAutoInterface]
public class SendOperationExecutor(
  ISdkActivityFactory activityFactory,
  ISdkMetricsFactory metricsFactory,
  ISerializeProcessFactory serializeProcessFactory,
  ISerializationOptions serializationOptions
) : ISendOperationExecutor
{
  public async Task<SerializeProcessResults> Send(
    Uri url,
    string streamId,
    string? authorizationToken,
    Base value,
    IProgress<ProgressArgs>? onProgressAction,
    CancellationToken cancellationToken
  )
  {
    using var receiveActivity = activityFactory.Start("Operations.Send");
    receiveActivity?.SetTag("speckle.url", url);
    receiveActivity?.SetTag("speckle.projectId", streamId);
    metricsFactory.CreateCounter<long>("Send").Add(1);

    var process = serializeProcessFactory.CreateSerializeProcess(
      url,
      streamId,
      authorizationToken,
      onProgressAction,
      cancellationToken,
      new SerializeProcessOptions()
      {
        SkipCacheRead = serializationOptions.SkipCacheRead,
        SkipCacheWrite = serializationOptions.SkipCacheWrite,
        SkipServer = serializationOptions.SkipServer,
        SkipFindTotalObjects = serializationOptions.SkipFindTotalObjects
      }
    );
    try
    {
      var results = await process.Serialize(value).ConfigureAwait(false);

      receiveActivity?.SetStatus(SdkActivityStatusCode.Ok);
      return results;
    }
    catch (OperationCanceledException)
    {
      //this is handled by the caller
      throw;
    }
    catch (Exception ex)
    {
      receiveActivity?.SetStatus(SdkActivityStatusCode.Error);
      receiveActivity?.RecordException(ex);
      throw;
    }
    finally
    {
      await process.DisposeAsync().ConfigureAwait(false);
    }
  }
}
