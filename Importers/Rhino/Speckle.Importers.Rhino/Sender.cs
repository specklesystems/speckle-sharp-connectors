﻿using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rhino;
using Rhino.DocObjects;
using Speckle.Connectors.Common.Operations;
using Speckle.Converters.Common;
using Speckle.Converters.Rhino;
using Speckle.Sdk.Logging;

namespace Speckle.Importers.Rhino;

public class Sender(
  ISdkActivityFactory activityFactory,
  IServiceProvider serviceProvider,
  IRhinoConversionSettingsFactory rhinoConversionSettingsFactory,
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

      var operation = scope.ServiceProvider.GetRequiredService<SendOperation<RhinoObject>>();
      var buildResults = await operation.Build(rhinoObjects, projectId, new Progress(), CancellationToken.None);
      var (results, versionId) = await operation.Send(
        buildResults.RootObject,
        serverUrl,
        projectId,
        modelId,
        token,
        string.Empty,
        new Progress(),
        CancellationToken.None
      );

      Console.WriteLine($"Root: {results.RootId}");

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
