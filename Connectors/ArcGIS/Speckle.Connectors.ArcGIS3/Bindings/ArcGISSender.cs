using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Mapping;
using Speckle.Autofac.DependencyInjection;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.Utils.Operations;
using Speckle.Converters.ArcGIS3;
using Speckle.Converters.ArcGIS3.Utils;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.ArcGIS.Bindings;

public interface IArcGISSender
{
  Task<SendOperationResult> SendOperation(
    IBridge parent,
    SenderModelCard modelCard,
    IReadOnlyList<MapMember> objects,
    CancellationToken ct = default
  );
}

public class ArcGISSender(
  IUnitOfWorkFactory unitOfWorkFactory,
  IOperationProgressManager operationProgressManager,
  IArcGISConversionSettingsFactory arcGisConversionSettingsFactory
) : ScopedSender(unitOfWorkFactory, operationProgressManager), IArcGISSender
{
  public async Task<SendOperationResult> SendOperation(
    IBridge parent,
    SenderModelCard modelCard,
    IReadOnlyList<MapMember> objects,
    CancellationToken ct = default
  )
  {
    var result = await base.SendOperation(
        parent,
        modelCard.GetSendInfo(Speckle.Connectors.Utils.Connector.Slug),
        modelCard.ModelCardId.NotNull(),
        objects,
        arcGisConversionSettingsFactory.Create(
          Project.Current,
          MapView.Active.Map,
          new CRSoffsetRotation(MapView.Active.Map)
        ),
        ct
      )
      .ConfigureAwait(false);
    return result.NotNull();
  }
}
