using Autodesk.Revit.DB;
using Speckle.Autofac.DependencyInjection;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.Revit.Operations.Send.Settings;
using Speckle.Connectors.Utils.Operations;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Settings;
using Speckle.InterfaceGenerator;
using Speckle.Sdk;

namespace Speckle.Connectors.Revit.Bindings;

[GenerateAutoInterface]
public class RevitSender(
  RevitContext revitContext,
  IUnitOfWorkFactory unitOfWorkFactory,
  ToSpeckleSettingsManager toSpeckleSettingsManager,
  IRevitConversionSettingsFactory revitConversionSettingsFactory
) : IRevitSender
{
  public async Task<SendOperationResult> SendOperation(
    SenderModelCard modelCard,
    IReadOnlyList<ElementId> objects,
    Action<string, double?>? onOperationProgressed,
    CancellationToken ct = default
  )
  {
    var activeUIDoc =
      revitContext.UIApplication?.ActiveUIDocument
      ?? throw new SpeckleException("Unable to retrieve active UI document");
    using var unitOfWork = unitOfWorkFactory.Create();
    using var settings = unitOfWork
      .Resolve<IConverterSettingsStore<RevitConversionSettings>>()
      .Push(
        () =>
          revitConversionSettingsFactory.Create(
            activeUIDoc.Document,
            toSpeckleSettingsManager.GetDetailLevelSetting(modelCard),
            toSpeckleSettingsManager.GetReferencePointSetting(modelCard)
          )
      );

    return await unitOfWork
      .Resolve<SendOperation<ElementId>>()
      .Execute(objects, modelCard.GetSendInfo(Speckle.Connectors.Utils.Connector.Slug), onOperationProgressed, ct)
      .ConfigureAwait(false);
  }
}
