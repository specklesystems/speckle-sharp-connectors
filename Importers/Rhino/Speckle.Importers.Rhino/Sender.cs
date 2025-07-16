using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rhino;
using Rhino.DocObjects;
using Speckle.Connectors.Common.Operations;
using Speckle.Converters.Common;
using Speckle.Converters.Rhino;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Logging;

namespace Speckle.Importers.Rhino;

public class Sender(
  ISdkActivityFactory activityFactory,
  IServiceProvider serviceProvider,
  IRhinoConversionSettingsFactory rhinoConversionSettingsFactory,
  IAccountFactory accountFactory,
  Progress progress,
  ILogger<Sender> logger
)
{
  public async Task<string?> Send(string projectId, string modelId, Uri serverUrl, string token)
  {
    using var activity = activityFactory.Start();
    using var scope = serviceProvider.CreateScope();
    scope
      .ServiceProvider.GetRequiredService<IConverterSettingsStore<RhinoConversionSettings>>()
      .Initialize(rhinoConversionSettingsFactory.Create(RhinoDoc.ActiveDoc));
    try
    {
      List<RhinoObject> rhinoObjects = RhinoDoc
        .ActiveDoc.Objects.GetObjectList(ObjectType.AnyObject)
        .Where(obj => obj != null)
        .ToList();

      if (rhinoObjects.Count == 0)
      {
        return null;
      }

      var account = await accountFactory.CreateAccount(serverUrl, token);
      var operation = scope.ServiceProvider.GetRequiredService<SendOperation<RhinoObject>>();
      var buildResults = await operation.Build(rhinoObjects, projectId, progress, CancellationToken.None);
      var (results, versionId) = await operation.Send(
        buildResults.RootObject,
        projectId,
        modelId,
        token,
        null,
        account,
        progress,
        CancellationToken.None
      );

      logger.LogInformation($"Root: {results.RootId}");

      return versionId;
    }
#pragma warning disable CA1031
    catch (Exception ex)
#pragma warning restore CA1031
    {
      logger.LogError(ex, "Error while sending");
    }

    return null;
  }
}
