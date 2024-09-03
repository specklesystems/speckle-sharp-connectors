using Rhino;
using Rhino.DocObjects;
using Speckle.Autofac.DependencyInjection;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.Utils.Builders;
using Speckle.Connectors.Utils.Operations;
using Speckle.Converters.Rhino;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.Rhino.Bindings;

public interface IRhinoSender
{
  Task<SendOperationResult> SendOperation(
    IBridge parent,
    SenderModelCard modelCard,
    IReadOnlyList<RhinoObject> objects,
    CancellationToken ct = default
  );
  Task<HostObjectBuilderResult> ReceiveOperation(
    IBridge parent,
    ReceiverModelCard modelCard,
    CancellationToken ct = default
  );
}

public class RhinoSender(
  IUnitOfWorkFactory unitOfWorkFactory,
  IOperationProgressManager operationProgressManager,
  IRhinoConversionSettingsFactory rhinoConversionSettingsFactory
) : ScopedSender(unitOfWorkFactory, operationProgressManager), IRhinoSender
{
  public async Task<SendOperationResult> SendOperation(
    IBridge parent,
    SenderModelCard modelCard,
    IReadOnlyList<RhinoObject> objects,
    CancellationToken ct = default
  )
  {
    var result = await base.SendOperation(
        parent,
        modelCard.GetSendInfo(Utils.Connector.Slug),
        modelCard.ModelCardId.NotNull(),
        objects,
        rhinoConversionSettingsFactory.Create(RhinoDoc.ActiveDoc),
        ct
      )
      .ConfigureAwait(false);
    return result;
  }

  public async Task<HostObjectBuilderResult> ReceiveOperation(
    IBridge parent,
    ReceiverModelCard modelCard,
    CancellationToken ct = default
  )
  {
    var result = await base.ReceiveOperation(
        parent,
        modelCard.GetReceiveInfo(Speckle.Connectors.Utils.Connector.Slug),
        modelCard.ModelCardId.NotNull(),
        rhinoConversionSettingsFactory.Create(RhinoDoc.ActiveDoc),
        ct
      )
      .ConfigureAwait(false);
    return result;
  }
}
