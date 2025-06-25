using System.Diagnostics;
using Grasshopper.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common.Analytics;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.GrasshopperShared.Components.BaseComponents;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;
using Speckle.Connectors.GrasshopperShared.Registration;
using Speckle.Sdk;
using Speckle.Sdk.Api;
using Speckle.Sdk.Common;
using Speckle.Sdk.Credentials;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations.Send;

public class SendComponentInput
{
  public SpeckleUrlModelResource Resource { get; }
  public SpeckleCollectionWrapperGoo Input { get; }
  public bool Run { get; }

  public SendComponentInput(SpeckleUrlModelResource resource, SpeckleCollectionWrapperGoo input, bool run)
  {
    Resource = resource;
    Input = input;
    Run = run;
  }
}

public class SendComponentOutput(SpeckleUrlModelResource? resource)
{
  public SpeckleUrlModelResource? Resource { get; } = resource;
}

public class SendComponent : SpeckleTaskCapableComponent<SendComponentInput, SendComponentOutput>
{
  public SendComponent()
    : base(
      "(Sync) Publish",
      "sP",
      "Publish a collection to Speckle, synchronously",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.DEVELOPER
    ) { }

  public override Guid ComponentGuid => new("0CF0D173-BDF0-4AC2-9157-02822B90E9FB");

  public string? Url { get; private set; }

  protected override Bitmap Icon => Resources.speckle_operations_syncpublish;

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddParameter(new SpeckleUrlModelResourceParam());
    pManager.AddParameter(
      new SpeckleCollectionParam(GH_ParamAccess.item),
      "Collection",
      "collection",
      "The model collection to publish",
      GH_ParamAccess.item
    );

    pManager.AddBooleanParameter("Run", "r", "Run the publish operation", GH_ParamAccess.item);
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    pManager.AddParameter(new SpeckleUrlModelResourceParam());
  }

  protected override SendComponentInput GetInput(IGH_DataAccess da)
  {
    if (da.Iteration != 0)
    {
      throw new SpeckleException("No more than 1 resource allowed");
    }

    SpeckleUrlModelResource? resource = null;
    if (!da.GetData(0, ref resource))
    {
      throw new SpeckleException("Failed to get resource");
    }

    SpeckleCollectionWrapperGoo rootCollectionWrapper = new();
    da.GetData(1, ref rootCollectionWrapper);

    bool run = false;
    da.GetData(2, ref run);

    return new SendComponentInput(resource.NotNull(), rootCollectionWrapper, run);
  }

  protected override void SetOutput(IGH_DataAccess da, SendComponentOutput result)
  {
    if (result.Resource is null)
    {
      Message = "Not Published";
    }
    else
    {
      da.SetData(0, result.Resource);
      Message = "Done";
    }
  }

  public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
  {
    base.AppendAdditionalMenuItems(menu);

    Menu_AppendSeparator(menu);
    if (Url != null)
    {
      Menu_AppendSeparator(menu);

      Menu_AppendItem(menu, $"View created model online ↗", (s, e) => Open(Url));
    }

    static void Open(string url)
    {
      var psi = new ProcessStartInfo { FileName = url, UseShellExecute = true };
      Process.Start(psi);
    }
  }

  protected override async Task<SendComponentOutput> PerformTask(
    SendComponentInput input,
    CancellationToken cancellationToken = default
  )
  {
    var multipleResources = Params.Input[0].VolatileData.HasInputCountGreaterThan(1);
    var multipleCollections = Params.Input[1].VolatileData.HasInputCountGreaterThan(1);

    var hasMultipleInputs = multipleCollections || multipleResources;

    if (hasMultipleInputs)
    {
      var mCollErrText =
        "Only one single collection supported. Please group your input collections into one single one before sending.";
      var mLinksErrText =
        "Only one single model can be published to from this node. To send to multiple models, please use multiple publish components.";

      if (multipleCollections)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, mCollErrText);
      }

      if (multipleResources)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, mLinksErrText);
      }

      return new(null);
    }

    if (!input.Run)
    {
      return new(null);
    }

    using var scope = PriorityLoader.CreateScopeForActiveDocument();
    var clientFactory = scope.ServiceProvider.GetRequiredService<IClientFactory>();
    var sendOperation = scope.ServiceProvider.GetRequiredService<SendOperation<SpeckleCollectionWrapperGoo>>();

    Account? account = input.Resource.Account.GetAccount(scope);
    if (account is null)
    {
      throw new SpeckleAccountManagerException($"No default account was found");
    }

    var progress = new Progress<CardProgress>(_ =>
    {
      // TODO: Progress only makes sense in non-blocking async receive, which is not supported yet.
      // Message = $"{progress.Status}: {progress.Progress}";
    });

    using var client = clientFactory.Create(account);
    var sendInfo = await input.Resource.GetSendInfo(client, cancellationToken).ConfigureAwait(false);
    var result = await sendOperation
      .Execute(new List<SpeckleCollectionWrapperGoo>() { input.Input }, sendInfo, progress, cancellationToken)
      .ConfigureAwait(false);

    // TODO: If we have NodeRun events later, better to have `ComponentTracker` to use across components
    var customProperties = new Dictionary<string, object>() { { "isAsync", false } };
    if (sendInfo.WorkspaceId != null)
    {
      customProperties.Add("workspace_id", sendInfo.WorkspaceId);
    }

    var mixpanel = PriorityLoader.Container.GetRequiredService<IMixPanelManager>();
    await mixpanel.TrackEvent(MixPanelEvents.Send, account, customProperties);

    SpeckleUrlLatestModelVersionResource createdVersionResource =
      new(
        new(sendInfo.Account.id, null, sendInfo.Account.serverInfo.url),
        sendInfo.WorkspaceId,
        sendInfo.ProjectId,
        sendInfo.ModelId
      );
    Url = $"{sendInfo.Account.serverInfo.url}/projects/{sendInfo.ProjectId}/models/{sendInfo.ModelId}";
    return new SendComponentOutput(createdVersionResource);
  }
}
