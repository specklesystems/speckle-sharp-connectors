using Autodesk.Revit.DB;
using Speckle.Autofac.DependencyInjection;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.Revit.Operations.Send.Settings;
using Speckle.Connectors.Utils.Builders;
using Speckle.Connectors.Utils.Operations;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Settings;
using Speckle.InterfaceGenerator;
using Speckle.Sdk;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.Revit.Bindings;

[GenerateAutoInterface]
public class RevitSender(
  RevitContext revitContext,
  IUnitOfWorkFactory unitOfWorkFactory,
  ToSpeckleSettingsManager toSpeckleSettingsManager,
  IRevitConversionSettingsFactory revitConversionSettingsFactory,
  IOperationProgressManager operationProgressManager
) : ScopedSender(unitOfWorkFactory, operationProgressManager), IRevitSender
{
  public async Task<SendOperationResult> SendOperation(
    IBridge parent,
    SenderModelCard modelCard,
    IReadOnlyList<ElementId> objects,
    CancellationToken ct = default
  )
  {
    var activeUIDoc =
      revitContext.UIApplication?.ActiveUIDocument
      ?? throw new SpeckleException("Unable to retrieve active UI document");
    var result = await base.SendOperation(
        parent,
        modelCard.GetSendInfo(Utils.Connector.Slug),
        modelCard.ModelCardId.NotNull(),
        objects,
        revitConversionSettingsFactory.Create(
          activeUIDoc.Document,
          toSpeckleSettingsManager.GetDetailLevelSetting(modelCard),
          toSpeckleSettingsManager.GetReferencePointSetting(modelCard)
        ),
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
    var activeUIDoc =
      revitContext.UIApplication?.ActiveUIDocument
      ?? throw new SpeckleException("Unable to retrieve active UI document");
    var result = await base.ReceiveOperation(
        parent,
        modelCard.GetReceiveInfo(Speckle.Connectors.Utils.Connector.Slug),
        modelCard.ModelCardId.NotNull(),
        revitConversionSettingsFactory.Create(
          activeUIDoc.Document,
          DetailLevelType.Coarse, //TODO figure out
          null
        ),
        ct
      )
      .ConfigureAwait(false);
    return result;
  }
}
