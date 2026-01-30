using System.Diagnostics;
using Grasshopper;
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
  public SpecklePropertyGroupGoo? RootProperties { get; }

  public SendComponentInput(
    SpeckleUrlModelResource resource,
    SpeckleCollectionWrapperGoo input,
    bool run,
    SpecklePropertyGroupGoo? rootProperties
  )
  {
    Resource = resource;
    Input = input;
    Run = run;
    RootProperties = rootProperties;
  }
}

public class SendComponentOutput(SpeckleUrlModelResource? resource, string? versionId = null)
{
  public SpeckleUrlModelResource? Resource { get; } = resource;
  public string? VersionId { get; } = versionId;
}

public class SendComponent : SpeckleTaskCapableComponent<SendComponentInput, SendComponentOutput>
{
  public override Guid ComponentGuid => new("0CF0D173-BDF0-4AC2-9157-02822B90E9FB");
  public string? Url { get; private set; }
  public string? VersionMessage { get; private set; }
  protected override Bitmap Icon => Resources.speckle_operations_syncpublish;

  public SendComponent()
    : base(
      "(Sync) Publish",
      "sP",
      "Publish a collection to Speckle, synchronously",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.DEVELOPER
    ) { }

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    // speckle model
    pManager.AddParameter(new SpeckleUrlModelResourceParam());

    // collection
    pManager.AddParameter(
      new SpeckleCollectionParam(GH_ParamAccess.item),
      "Collection",
      "collection",
      "The model collection to publish",
      GH_ParamAccess.item
    );
    pManager.AddTextParameter("Version Message", "versionMessage", "The version message", GH_ParamAccess.item);
    pManager[2].Optional = true;

    // model-wide props (see cnx-2722)
    pManager.AddParameter(
      new SpecklePropertyGroupParam(),
      "Properties",
      "properties",
      "Optional model-wide properties to attach to the root collection",
      GH_ParamAccess.item
    );
    pManager[3].Optional = true;

    pManager.AddBooleanParameter("Run", "r", "Run the publish operation", GH_ParamAccess.item);
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    pManager.AddParameter(new SpeckleUrlModelResourceParam());
    pManager.AddTextParameter("Version ID", "V", "ID of the created version", GH_ParamAccess.item);
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

    string? versionMessage = null;
    da.GetData(2, ref versionMessage);
    VersionMessage = versionMessage;

    SpecklePropertyGroupGoo? rootPropsGoo = null;
    da.GetData(3, ref rootPropsGoo);

    // validate single properties group
    // we can't support a list input here, what does that even mean? grafting the collection to each props entry?? scary.
    if (Params.Input[3].VolatileData.DataCount > 1)
    {
      throw new SpeckleException("Only one Model Properties group is allowed");
    }

    bool run = false;
    da.GetData(4, ref run);

    return new SendComponentInput(resource.NotNull(), rootCollectionWrapper, run, rootPropsGoo);
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
      da.SetData(1, result.VersionId);
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

      Menu_AppendItem(menu, "View created model online ↗", (s, e) => Open(Url));
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

    // safe to always create new wrapper since users cannot create SpeckleRootCollectionWrapper directly - it's only
    // constructed here from the Collection + Model Properties inputs.
    // if this changes, then we need to update below!
    var rootWrapper = new SpeckleRootCollectionWrapper(input.Input.Value, input.RootProperties?.Unwrap());
    var collectionToSend = new SpeckleRootCollectionWrapperGoo(rootWrapper);

    using var scope = PriorityLoader.CreateScopeForActiveDocument();
    var clientFactory = scope.ServiceProvider.GetRequiredService<IClientFactory>();
    var sendOperation = scope.ServiceProvider.GetRequiredService<SendOperation<SpeckleCollectionWrapperGoo>>();

    Account? account = input.Resource.Account.GetAccount(scope);
    if (account is null)
    {
      throw new SpeckleAccountManagerException("No default account was found");
    }

    var (fileName, fileBytes) = GetGrasshopperFileInfo();

    var progress = new Progress<CardProgress>(_ =>
    {
      // TODO: Progress only makes sense in non-blocking async receive, which is not supported yet.
      // Message = $"{progress.Status}: {progress.Progress}";
    });

    using var client = clientFactory.Create(account);
    var sendInfo = await input.Resource.GetSendInfo(client, cancellationToken).ConfigureAwait(false);
    var (result, versionId) = await sendOperation
      .Send([collectionToSend], sendInfo, fileName, fileBytes, VersionMessage, progress, cancellationToken)
      .ConfigureAwait(false);

    // TODO: If we have NodeRun events later, better to have `ComponentTracker` to use across components
    var customProperties = new Dictionary<string, object> { { "isAsync", false } };
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
    return new SendComponentOutput(createdVersionResource, versionId);
  }

  public static (string? fileName, long? fileSizeBytes) GetGrasshopperFileInfo()
  {
    var doc = Instances.ActiveCanvas?.Document;

    if (doc is null || !File.Exists(doc.FilePath))
    {
      return (null, null);
    }
    var fileInfo = new FileInfo(doc.FilePath);

    return (fileInfo.Name, fileInfo.Length);
  }
}
