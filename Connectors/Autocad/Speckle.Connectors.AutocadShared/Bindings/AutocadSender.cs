using Speckle.Autofac.DependencyInjection;
using Speckle.Connectors.Autocad.Operations.Send;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.Utils.Operations;
using Speckle.Converters.Autocad;
using Speckle.InterfaceGenerator;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.Autocad.Bindings;

[GenerateAutoInterface]
public class AutocadSender(
  IUnitOfWorkFactory unitOfWorkFactory,
  IOperationProgressManager operationProgressManager,
  IAutocadConversionSettingsFactory autocadConversionSettingsFactory
) : ScopedSender(unitOfWorkFactory, operationProgressManager), IAutocadSender
{
  public async Task<SendOperationResult> SendOperation(
    IBridge parent,
    SenderModelCard modelCard,
    Document document,
    IReadOnlyList<AutocadRootObject> objects,
    CancellationToken ct = default
  )
  {
    var result = await base.SendOperation(
        parent,
        modelCard.GetSendInfo(Utils.Connector.Slug),
        modelCard.ModelCardId.NotNull(),
        objects,
        autocadConversionSettingsFactory.Create(document),
        ct
      )
      .ConfigureAwait(false);
    return result;
  }
}
