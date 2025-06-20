using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.DUI.Models;
using Speckle.InterfaceGenerator;
using Speckle.Sdk;

namespace Speckle.Connectors.DUI.Bindings;

[GenerateAutoInterface]
public class ReceiveOperationManagerFactory(
  IServiceProvider serviceProvider,
  IOperationProgressManager operationProgressManager,
  DocumentModelStore store,
  ICancellationManager cancellationManager,
  ISpeckleApplication speckleApplication,
  IAccountService accountService,
  ILoggerFactory loggerFactory
) : IReceiveOperationManagerFactory
{
  public IReceiveOperationManager Create() =>
    new ReceiveOperationManager(
#pragma warning disable CA2000
      serviceProvider.CreateScope(),
#pragma warning restore CA2000
      cancellationManager,
      speckleApplication,
      store,
      operationProgressManager,
      accountService,
      loggerFactory.CreateLogger<ReceiveOperationManager>()
    );
}
