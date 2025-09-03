using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rhino;
using Rhino.DocObjects;
using Speckle.Connectors.Common.Analytics;
using Speckle.Connectors.Common.Operations;
using Speckle.Converters.Common;
using Speckle.Converters.Rhino;
using Speckle.Sdk;
using Speckle.Sdk.Api.GraphQL.Models;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Logging;
using Version = Speckle.Sdk.Api.GraphQL.Models.Version;

namespace Speckle.Importers.Rhino.Internal;

internal sealed class Sender(
  ISdkActivityFactory activityFactory,
  IServiceProvider serviceProvider,
  IRhinoConversionSettingsFactory rhinoConversionSettingsFactory,
  IMixPanelManager mixpanel,
  Progress progress,
  Application applicationInfo,
  ILogger<Sender> logger
)
{
  public async Task<Version> Send(Project project, string modelId, Account account, CancellationToken cancellationToken)
  {
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
    var buildResults = await operation.Build(rhinoObjects, project.id, progress, cancellationToken);
    var (results, version) = await operation.Send(
      buildResults.RootObject,
      project.id,
      modelId,
      applicationInfo.Slug,
      null,
      account,
      progress,
      cancellationToken
    );

    Dictionary<string, object> customProperties = [];
    customProperties.Add("actionSource", "import");
    if (project.workspaceId != null)
    {
      customProperties.Add("workspace_id", project.workspaceId);
    }

    await mixpanel.TrackEvent(MixPanelEvents.Send, account, customProperties);
    logger.LogInformation($"Root: {results.RootId}");

    return version;
  }
}
