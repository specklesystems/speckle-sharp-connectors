using Microsoft.Extensions.DependencyInjection;
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
  public async Task Send()
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
        return;
      }
      var projectId = "f3a42bdf24";
      var modelId = "99823dc746";
      var url = new Uri("https://app.speckle.systems");

      var sendInfo = new SendInfo("", url, projectId, modelId, string.Empty);

      var operation = scope.ServiceProvider.GetRequiredService<SendOperation<RhinoObject>>();
      var buildResults = await operation.Build(rhinoObjects, sendInfo, new Progress(), CancellationToken.None);
      var (results, _) = await operation.Send(
        buildResults.RootObject,
        sendInfo,
        new Progress(),
        CancellationToken.None
      );

      Console.WriteLine($"Root: {results.RootId}");
    }
#pragma warning disable CA1031
    catch (Exception ex)
#pragma warning restore CA1031
    {
      logger.LogError(ex, "Error while sending");
    }
  }
}
