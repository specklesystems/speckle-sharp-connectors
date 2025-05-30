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

public class SendComponent : SpeckleScopedTaskCapableComponent<SendComponentInput, SendComponentOutput>
{
  private readonly IMixPanelManager _mixpanel;

  public SendComponent()
    : base(
      "(Sync) Publish",
      "sP",
      "Publish a collection to Speckle, synchronously",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.DEVELOPER
    )
  {
    _mixpanel = PriorityLoader.Container.GetRequiredService<IMixPanelManager>();
  }

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

  protected override async Task<SendComponentOutput> PerformScopedTask(
    SendComponentInput input,
    IServiceScope scope,
    CancellationToken cancellationToken = default
  )
  {
    if (!input.Run)
    {
      return new(null);
    }

    var accountService = scope.ServiceProvider.GetRequiredService<IAccountService>();
    var accountManager = scope.ServiceProvider.GetRequiredService<IAccountManager>();
    var clientFactory = scope.ServiceProvider.GetRequiredService<IClientFactory>();
    var sendOperation = scope.ServiceProvider.GetRequiredService<SendOperation<SpeckleCollectionWrapperGoo>>();

    Account? account =
      input.Resource.AccountId != null
        ? accountManager.GetAccount(input.Resource.AccountId)
        : accountService.GetAccountWithServerUrlFallback("", new Uri(input.Resource.Server)); // fallback the account that matches with URL if any

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
    await _mixpanel.TrackEvent(MixPanelEvents.Send, account, customProperties);

    SpeckleUrlLatestModelVersionResource createdVersionResource =
      new(
        sendInfo.AccountId,
        sendInfo.ServerUrl.ToString(),
        sendInfo.WorkspaceId,
        sendInfo.ProjectId,
        sendInfo.ModelId
      );
    Url = $"{createdVersionResource.Server}projects/{sendInfo.ProjectId}/models/{sendInfo.ModelId}"; // TODO: missing "@VersionId"

    return new SendComponentOutput(createdVersionResource);
  }
}
