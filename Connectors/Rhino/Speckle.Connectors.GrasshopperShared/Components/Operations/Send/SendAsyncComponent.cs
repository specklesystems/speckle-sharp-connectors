using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Timers;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using GrasshopperAsyncComponent;
using Microsoft.Extensions.DependencyInjection;
using Rhino;
using Speckle.Connectors.Common.Analytics;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;
using Speckle.Connectors.GrasshopperShared.Registration;
using Speckle.Sdk;
using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Models;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Models.Extensions;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations.Send;

[Guid("52481972-7867-404F-8D9F-E1481183F355")]
public class SendAsyncComponent : GH_AsyncComponent<SendAsyncComponent>
{
  private ResourceCollection<Project>? LastFetchedProjects { get; set; }
  private ResourceCollection<Model>? LastFetchedModels { get; set; }

  public GhContextMenuButton ProjectContextMenuButton { get; set; }
  public GhContextMenuButton ModelContextMenuButton { get; set; }

  private ToolStripDropDown? ProjectDropDown { get; set; }
  private ToolStripDropDown? ModelDropDown { get; set; }

  public SendAsyncComponent()
    : base(
      "Publish",
      "P",
      "Publish a collection to Speckle",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.OPERATIONS
    )
  {
    BaseWorker = new SendComponentWorker(this);
    Attributes = new SendAsyncComponentAttributes(this);
  }

  public override Guid ComponentGuid => GetType().GUID;

  protected override Bitmap Icon => Resources.speckle_operations_publish;

  public ComponentState CurrentComponentState { get; set; } = ComponentState.NeedsInput;
  public bool AutoSend { get; set; }
  public bool JustPastedIn { get; set; }
  public double OverallProgress { get; set; }
  public string? Url { get; set; }
  public IClient ApiClient { get; set; }
  public HostApp.SpeckleUrlModelResource? UrlModelResource { get; set; }
  public SpeckleCollectionWrapperGoo? RootCollectionWrapper { get; set; }

  public SpeckleUrlModelResource? OutputParam { get; set; }

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddParameter(new SpeckleUrlModelResourceParam());
    pManager.AddParameter(
      new SpeckleCollectionParam(GH_ParamAccess.item),
      "Collection",
      "collection",
      "The collection model object to send",
      GH_ParamAccess.item
    );
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    pManager.AddParameter(new SpeckleUrlModelResourceParam());
  }

  public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
  {
    static void Open(string url)
    {
      var psi = new ProcessStartInfo { FileName = url, UseShellExecute = true };
      Process.Start(psi);
    }

    base.AppendAdditionalMenuItems(menu);

    Menu_AppendSeparator(menu);

    var autoSendMi = Menu_AppendItem(
      menu,
      "Publish automatically",
      (s, e) =>
      {
        AutoSend = !AutoSend;
        RhinoApp.InvokeOnUiThread(
          (Action)
            delegate
            {
              OnDisplayExpired(true);
            }
        );
      },
      true,
      AutoSend
    );
    autoSendMi.ToolTipText =
      "Toggle automatic data publishing. If set, any change in any of the input parameters of this component will start publishing.\n Please be aware that if a new publish starts before an old one is finished, the previous operation is cancelled.";

    if (Url != null)
    {
      Menu_AppendSeparator(menu);

      Menu_AppendItem(menu, $"View created model online ↗", (s, e) => Open(Url));
    }

    Menu_AppendSeparator(menu);

    if (CurrentComponentState == ComponentState.Sending)
    {
      Menu_AppendItem(
        menu,
        "Cancel Publish",
        (s, e) =>
        {
          CurrentComponentState = ComponentState.Expired;
          RequestCancellation();
        }
      );
    }
  }

  protected override void SolveInstance(IGH_DataAccess da)
  {
    using var scope = PriorityLoader.CreateScopeForActiveDocument();

    var accountService = scope.ServiceProvider.GetRequiredService<IAccountService>();
    var accountManager = scope.ServiceProvider.GetRequiredService<IAccountManager>();
    var clientFactory = scope.ServiceProvider.GetRequiredService<IClientFactory>();

    // We need to call this always in here to be able to react and set events :/
    ParseInput(da, accountService, accountManager, clientFactory);

    if (
      (AutoSend || CurrentComponentState == ComponentState.Ready || CurrentComponentState == ComponentState.Sending)
      && !JustPastedIn
    )
    {
      CurrentComponentState = ComponentState.Sending;

      // Delegate control to parent async component.
      base.SolveInstance(da);
      return;
    }

    if (JustPastedIn)
    {
      // Set output data in a "first run" event. Note: we are not persisting the actual "sent" object as it can be very big.
      base.SolveInstance(da);
      return;
    }
    else
    {
      da.SetData(0, OutputParam);
      CurrentComponentState = ComponentState.Expired;
      Message = "Expired";
      OnDisplayExpired(true);
    }
  }

  public override void RemovedFromDocument(GH_Document document)
  {
    RequestCancellation();
    base.RemovedFromDocument(document);
  }

  public override void DisplayProgress(object sender, ElapsedEventArgs e)
  {
    if (Workers.Count == 0)
    {
      return;
    }

    Message = "";
    var total = 0.0;
    foreach (var kvp in ProgressReports)
    {
      Message += $"{kvp.Key}: {kvp.Value}\n";
      total += kvp.Value;
    }

    OverallProgress = total / ProgressReports.Keys.Count;

    RhinoApp.InvokeOnUiThread(
      (Action)
        delegate
        {
          OnDisplayExpired(true);
        }
    );
  }

  public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
  {
    switch (context)
    {
      case GH_DocumentContext.Loaded:
        OnDisplayExpired(true);
        break;

      case GH_DocumentContext.Unloaded:
        // Will execute every time a document becomes inactive (in background or closing file.)
        //Correctly dispose of the client when changing documents to prevent subscription handlers being called in background.
        RequestCancellation();
        break;
    }

    base.DocumentContextChanged(document, context);
  }

  private void ParseInput(
    IGH_DataAccess da,
    IAccountService accountService,
    IAccountManager accountManager,
    IClientFactory clientFactory
  )
  {
    HostApp.SpeckleUrlModelResource? dataInput = null;
    da.GetData(0, ref dataInput);
    if (dataInput is null)
    {
      UrlModelResource = null;
      TriggerAutoSave();
      return;
    }

    UrlModelResource = dataInput;
    try
    {
      Account? account =
        dataInput.AccountId != null
          ? accountManager.GetAccount(dataInput.AccountId)
          : accountService.GetAccountWithServerUrlFallback("", new Uri(dataInput.Server)); // fallback the account that matches with URL if any
      if (account is null)
      {
        throw new SpeckleAccountManagerException($"No default account was found");
      }

      ApiClient?.Dispose();
      ApiClient = clientFactory.Create(account);
    }
    catch (Exception e) when (!e.IsFatal())
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.ToFormattedString());
    }

    SpeckleCollectionWrapperGoo rootCollectionWrapper = new();
    da.GetData(1, ref rootCollectionWrapper);
    if (rootCollectionWrapper is null)
    {
      RootCollectionWrapper = null;
      TriggerAutoSave();
      return;
    }
    RootCollectionWrapper = rootCollectionWrapper;
  }
}

public class SendComponentWorker : WorkerInstance<SendAsyncComponent>
{
  public SendComponentWorker(
    SendAsyncComponent p,
    string id = "baseWorker",
    CancellationToken cancellationToken = default
  )
    : base(p, id, cancellationToken) { }

  private Stopwatch? _stopwatch;
  public SpeckleUrlModelResource? OutputParam { get; set; }
  private List<(GH_RuntimeMessageLevel, string)> RuntimeMessages { get; } = new();

  public override WorkerInstance<SendAsyncComponent> Duplicate(string id, CancellationToken cancellationToken)
  {
    return new SendComponentWorker(Parent, id, cancellationToken);
  }

  public override void GetData(IGH_DataAccess da, GH_ComponentParamServer p)
  {
    _stopwatch = new Stopwatch();
    _stopwatch.Start();
  }

  public override void SetData(IGH_DataAccess da)
  {
    _stopwatch?.Stop();

    if (Parent.JustPastedIn)
    {
      Parent.JustPastedIn = false;
      da.SetData(0, Parent.OutputParam);
      return;
    }

    if (CancellationToken.IsCancellationRequested)
    {
      Parent.CurrentComponentState = ComponentState.Expired;
      return;
    }

    foreach (var (level, message) in RuntimeMessages)
    {
      Parent.AddRuntimeMessage(level, message);
    }

    da.SetData(0, OutputParam);

    Parent.CurrentComponentState = ComponentState.UpToDate;
    Parent.OutputParam = OutputParam; // ref the outputs in the parent too, so we can serialise them on write/read
    Parent.OverallProgress = 0;

    var hasWarnings = RuntimeMessages.Count > 0;
    if (!hasWarnings)
    {
      /* POC: cannot use GetTotalChildrenCount() on the root collection, because this contains subcollection wrappers which are not recognized by our typeloader. Will throw exception as result.
      Parent.AddRuntimeMessage(
        GH_RuntimeMessageLevel.Remark,
        $"Successfully published {((SendAsyncComponent)Parent).RootCollectionWrapper?.Value.Collection.GetTotalChildrenCount()} objects to Speckle."
      );
      */
      Parent.AddRuntimeMessage(
        GH_RuntimeMessageLevel.Remark,
        $"Successfully published to Speckle. Right-click on the component to view online."
      );
      Parent.AddRuntimeMessage(
        GH_RuntimeMessageLevel.Remark,
        $"Publish duration: {_stopwatch?.ElapsedMilliseconds / 1000f}s"
      );
    }
  }

  public override async Task DoWork(Action<string, double> reportProgress, ComponentDoneCallback done)
  {
    if (Parent.JustPastedIn)
    {
      return;
    }

    try
    {
      await Send(reportProgress);
      done();
    }
    catch (OperationCanceledException) when (CancellationToken.IsCancellationRequested)
    {
      Parent.CurrentComponentState = ComponentState.Expired;
      //No need to call `done()` - GrasshopperAsyncComponent assumes immediate cancel,
      //thus it has already performed clean-up actions that would normally be done on `done()`
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      RuntimeMessages.Add((GH_RuntimeMessageLevel.Error, ex.ToFormattedString()));
      done();
    }
  }

  private async Task Send(Action<string, double> reportProgress)
  {
    SpeckleUrlModelResource? urlModelResource = Parent.UrlModelResource;
    if (urlModelResource is null)
    {
      throw new InvalidOperationException("Url Resource was null");
    }

    SpeckleCollectionWrapperGoo? rootCollectionWrapper = Parent.RootCollectionWrapper;
    if (rootCollectionWrapper is null)
    {
      throw new InvalidOperationException("Root Collection was null");
    }

    // Step 1 - SEND TO SERVER
    var sendInfo = await urlModelResource.GetSendInfo(Parent.ApiClient, CancellationToken).ConfigureAwait(false);

    var progress = new Progress<CardProgress>(p =>
    {
      reportProgress(Id, p.Progress ?? 0);
      //sendComponent.Message = $"{p.Status}";
    });

    using var scope = PriorityLoader.CreateScopeForActiveDocument();
    var sendOperation = scope.ServiceProvider.GetRequiredService<SendOperation<SpeckleCollectionWrapperGoo>>();
    SendOperationResult? result = await sendOperation.Execute(
      new List<SpeckleCollectionWrapperGoo>() { rootCollectionWrapper },
      sendInfo,
      progress,
      CancellationToken
    );

    // TODO: If we have NodeRun events later, better to have `ComponentTracker` to use across components
    var customProperties = new Dictionary<string, object>() { { "isAsync", true }, { "auto", Parent.AutoSend } };
    if (sendInfo.WorkspaceId != null)
    {
      customProperties.Add("workspace_id", sendInfo.WorkspaceId);
    }

    var mixPanelManager = scope.ServiceProvider.GetRequiredService<IMixPanelManager>();
    await mixPanelManager.TrackEvent(MixPanelEvents.Send, Parent.ApiClient.Account, customProperties);

    SpeckleUrlModelVersionResource createdVersion =
      new(
        sendInfo.AccountId,
        sendInfo.ServerUrl.ToString(),
        sendInfo.WorkspaceId,
        sendInfo.ProjectId,
        sendInfo.ModelId,
        result.VersionId
      );
    OutputParam = createdVersion;
    Parent.Url = $"{createdVersion.Server}projects/{sendInfo.ProjectId}/models/{sendInfo.ModelId}";
  }
}

public class SendAsyncComponentAttributes : GH_ComponentAttributes
{
  private bool _selected;

  public SendAsyncComponentAttributes(GH_Component owner)
    : base(owner) { }

  private Rectangle ButtonBounds { get; set; }

  public override bool Selected
  {
    get => _selected;
    set => _selected = value;
  }

  protected override void Layout()
  {
    base.Layout();

    var baseRec = GH_Convert.ToRectangle(Bounds);
    baseRec.Height += 26;

    var btnRec = baseRec;
    btnRec.Y = btnRec.Bottom - 26;
    btnRec.Height = 26;
    btnRec.Inflate(-2, -2);

    Bounds = baseRec;
    ButtonBounds = btnRec;
  }

  protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
  {
    base.Render(canvas, graphics, channel);

    var state = ((SendAsyncComponent)Owner).CurrentComponentState;

    if (channel == GH_CanvasChannel.Objects)
    {
      if (((SendAsyncComponent)Owner).AutoSend)
      {
        using var autoSendButton = GH_Capsule.CreateTextCapsule(
          ButtonBounds,
          ButtonBounds,
          GH_Palette.Blue,
          "Auto Publish",
          2,
          0
        );

        autoSendButton.Render(graphics, Selected, Owner.Locked, false);
      }
      else
      {
        var palette =
          state == ComponentState.Expired || state == ComponentState.UpToDate
            ? GH_Palette.Black
            : GH_Palette.Transparent;

        var text = state == ComponentState.Sending ? "Publishing..." : "Publish";

        using var button = GH_Capsule.CreateTextCapsule(
          ButtonBounds,
          ButtonBounds,
          palette,
          text,
          2,
          state == ComponentState.Expired ? 10 : 0
        );
        button.Render(graphics, Selected, Owner.Locked, false);
      }
    }
  }

  public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
  {
    if (e.Button == MouseButtons.Left)
    {
      if (((RectangleF)ButtonBounds).Contains(e.CanvasLocation))
      {
        if (((SendAsyncComponent)Owner).AutoSend)
        {
          ((SendAsyncComponent)Owner).AutoSend = false;
          Owner.OnDisplayExpired(true);
          return GH_ObjectResponse.Handled;
        }
        if (((SendAsyncComponent)Owner).CurrentComponentState == ComponentState.Sending)
        {
          return GH_ObjectResponse.Handled;
        }
        ((SendAsyncComponent)Owner).CurrentComponentState = ComponentState.Ready;
        Owner.ExpireSolution(true);
        return GH_ObjectResponse.Handled;
      }
    }

    return base.RespondToMouseDown(sender, e);
  }
}
