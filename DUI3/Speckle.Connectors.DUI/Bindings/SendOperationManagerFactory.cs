using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.DUI.Models;
using Speckle.InterfaceGenerator;
using Speckle.Sdk.Api;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Logging;

namespace Speckle.Connectors.DUI.Bindings;

[GenerateAutoInterface]
public class SendOperationManagerFactory(
  IServiceProvider serviceProvider,
  IOperationProgressManager operationProgressManager,
  DocumentModelStore store,
  ICancellationManager cancellationManager,
  ISdkActivityFactory activityFactory,
  IClientFactory clientFactory,
  IAccountManager accountManager,
  ILoggerFactory loggerFactory
) : ISendOperationManagerFactory
{
  public ISendOperationManager Create() =>
    new SendOperationManager(
#pragma warning disable CA2000
      serviceProvider.CreateScope(),
#pragma warning restore CA2000
      operationProgressManager,
      store,
      cancellationManager,
      activityFactory,
      clientFactory,
      accountManager,
      loggerFactory.CreateLogger<SendOperationManager>()
    );
}
