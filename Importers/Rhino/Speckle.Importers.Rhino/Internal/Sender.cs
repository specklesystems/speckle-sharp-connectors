using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rhino;
using Rhino.DocObjects;
using Speckle.Connectors.Common.Analytics;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Operations.Send;
using Speckle.Converters.Common;
using Speckle.Converters.Rhino;
using Speckle.Sdk;
using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Models;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Serialisation.V2.Send;

namespace Speckle.Importers.Rhino.Internal;

internal sealed class Sender(
  ISdkActivityFactory activityFactory,
  IServiceProvider serviceProvider,
  IRhinoConversionSettingsFactory rhinoConversionSettingsFactory,
  IMixPanelManager mixpanel,
  IIngestionProgressManagerFactory progressManagerFactory,
  ILogger<Sender> logger
)
{
  public async Task<SerializeProcessResults> Send(
    Project project,
    ModelIngestion ingestion,
    IClient speckleClient,
    CancellationToken cancellationToken
  )
  {
    var progressManager = progressManagerFactory.CreateInstance(
      speckleClient,
      ingestion,
      project.id,
      TimeSpan.FromSeconds(1.5),
      cancellationToken
    );
    // NOTE: introduction of AddVisualizationProperties setting not accounted for, hence hardcoded as true (i.e. "as before")
    using var activity = activityFactory.Start();
    using var scope = serviceProvider.CreateScope();
    scope
      .ServiceProvider.GetRequiredService<IConverterSettingsStore<RhinoConversionSettings>>()
      .Initialize(rhinoConversionSettingsFactory.Create(RhinoDoc.ActiveDoc, true));

    List<RhinoObject> rhinoObjects = RhinoDoc
      .ActiveDoc.Objects.GetObjectList(ObjectType.AnyObject)
      .Where(obj => obj != null)
      .ToList();

    if (rhinoObjects.Count == 0)
    {
      throw new SpeckleException("There are no objects found in the file");
    }

    var operation = scope.ServiceProvider.GetRequiredService<SendOperation<RhinoObject>>();
    var buildResults = await operation.Build(rhinoObjects, project.id, progressManager, cancellationToken);
    var results = await operation.SendObjects(
      buildResults.RootObject,
      project.id,
      speckleClient.Account,
      progressManager,
      cancellationToken
    );

    await TrackSendMetrics(project, speckleClient.Account);
    logger.LogInformation("Root: {RootId}", results.RootId);

    return results;
  }

  private async Task TrackSendMetrics(Project project, Account account)
  {
    Dictionary<string, object> customProperties = [];
    customProperties.Add("actionSource", "import");
    if (project.workspaceId != null)
    {
      customProperties.Add("workspace_id", project.workspaceId);
    }

    await mixpanel.TrackEvent(MixPanelEvents.Send, account, customProperties);
  }
}
