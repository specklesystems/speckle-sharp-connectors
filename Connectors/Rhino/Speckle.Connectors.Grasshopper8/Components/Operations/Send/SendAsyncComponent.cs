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
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Grasshopper8.HostApp;
using Speckle.Connectors.Grasshopper8.Parameters;
using Speckle.Connectors.Grasshopper8.Registration;
using Speckle.Converters.Common;
using Speckle.Converters.Rhino;
using Speckle.Sdk;
using Speckle.Sdk.Api;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Models.Extensions;

namespace Speckle.Connectors.Grasshopper8.Components.Operations.Send;

[Guid("52481972-7867-404F-8D9F-E1481183F355")]
public class SendAsyncComponent : GH_AsyncComponent
{
  public SendAsyncComponent()
    : base(
      "Async Send",
      "aS",
      "Send objects async to speckle",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.OPERATIONS
    )
  {
    BaseWorker = new SendComponentWorker(this);
    Attributes = new SendAsyncComponentAttributes(this);
  }

  public override Guid ComponentGuid => GetType().GUID;

  protected override Bitmap Icon => BitmapBuilder.CreateSquareIconBitmap("aS");

  public ComponentState CurrentComponentState { get; set; } = ComponentState.NeedsInput;
  public bool AutoSend { get; set; }
  public bool JustPastedIn { get; set; }
  public double OverallProgress { get; set; }
  public string? Url { get; set; }
  public Client ApiClient { get; set; }
  public HostApp.SpeckleUrlModelResource? UrlModelResource { get; set; }
  public SpeckleCollectionWrapperGoo? RootCollection { get; set; }

  public SpeckleUrlModelResource? OutputParam { get; set; }
  public SendOperation<SpeckleCollectionWrapperGoo> SendOperation { get; private set; }
  public static IServiceScope? Scope { get; set; }

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddParameter(new SpeckleUrlModelResourceParam());
    pManager.AddParameter(
      new SpeckleCollectionParam(GH_ParamAccess.item),
      "Model",
      "model",
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
      "Send automatically",
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
      "Toggle automatic data sending. If set, any change in any of the input parameters of this component will start sending.\n Please be aware that if a new send starts before an old one is finished, the previous operation is cancelled.";

    if (Url != null)
    {
      Menu_AppendSeparator(menu);

      Menu_AppendItem(menu, $"View created version online ↗", (s, e) => Open(Url));
    }

    Menu_AppendSeparator(menu);

    if (CurrentComponentState == ComponentState.Sending)
    {
      Menu_AppendItem(
        menu,
        "Cancel Send",
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
    // Dependency Injection
    Scope = PriorityLoader.Container.CreateScope();
    SendOperation = Scope.ServiceProvider.GetRequiredService<SendOperation<SpeckleCollectionWrapperGoo>>();
    var rhinoConversionSettingsFactory = Scope.ServiceProvider.GetRequiredService<IRhinoConversionSettingsFactory>();
    Scope
      .ServiceProvider.GetRequiredService<IConverterSettingsStore<RhinoConversionSettings>>()
      .Initialize(rhinoConversionSettingsFactory.Create(RhinoDoc.ActiveDoc));

    var accountManager = Scope.ServiceProvider.GetRequiredService<AccountService>();
    var clientFactory = Scope.ServiceProvider.GetRequiredService<IClientFactory>();

    // We need to call this always in here to be able to react and set events :/
    ParseInput(da, accountManager, clientFactory);

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
    Scope?.Dispose();
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

  private void ParseInput(IGH_DataAccess da, AccountService accountManager, IClientFactory clientFactory)
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
      // TODO: Get any account for this server, as we don't have a mechanism yet to pass accountIds through
      Account account = accountManager.GetAccountWithServerUrlFallback("", new Uri(dataInput.Server));
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
      RootCollection = null;
      TriggerAutoSave();
      return;
    }
    RootCollection = rootCollectionWrapper;
  }
}

public class SendComponentWorker : WorkerInstance
{
  public SendComponentWorker(GH_Component p)
    : base(p) { }

  private Stopwatch _stopwatch;
  public SpeckleUrlModelResource? OutputParam { get; set; }
  private List<(GH_RuntimeMessageLevel, string)> RuntimeMessages { get; } = new();

  public override WorkerInstance Duplicate()
  {
    return new SendComponentWorker(Parent);
  }

  public override void GetData(IGH_DataAccess da, GH_ComponentParamServer p)
  {
    _stopwatch = new Stopwatch();
    _stopwatch.Start();
  }

  public override void SetData(IGH_DataAccess da)
  {
    _stopwatch.Stop();

    if (((SendAsyncComponent)Parent).JustPastedIn)
    {
      ((SendAsyncComponent)Parent).JustPastedIn = false;
      da.SetData(0, ((SendAsyncComponent)Parent).OutputParam);
      return;
    }

    if (CancellationToken.IsCancellationRequested)
    {
      ((SendAsyncComponent)Parent).CurrentComponentState = ComponentState.Expired;
      return;
    }

    foreach (var (level, message) in RuntimeMessages)
    {
      Parent.AddRuntimeMessage(level, message);
    }

    da.SetData(0, OutputParam);

    ((SendAsyncComponent)Parent).CurrentComponentState = ComponentState.UpToDate;
    ((SendAsyncComponent)Parent).OutputParam = OutputParam; // ref the outputs in the parent too, so we can serialise them on write/read
    ((SendAsyncComponent)Parent).OverallProgress = 0;

    var hasWarnings = RuntimeMessages.Count > 0;
    if (!hasWarnings)
    {
      Parent.AddRuntimeMessage(
        GH_RuntimeMessageLevel.Remark,
        $"Successfully sent {((SendAsyncComponent)Parent).RootCollection?.Value.GetTotalChildrenCount()} objects to Speckle."
      );
      Parent.AddRuntimeMessage(
        GH_RuntimeMessageLevel.Remark,
        $"Send duration: {_stopwatch.ElapsedMilliseconds / 1000f}s"
      );
    }
  }

  public override void DoWork(Action<string, double> reportProgress, Action done)
  {
    var sendComponent = (SendAsyncComponent)Parent;

    if (sendComponent.JustPastedIn)
    {
      done();
      return;
    }

    if (CancellationToken.IsCancellationRequested)
    {
      sendComponent.CurrentComponentState = ComponentState.Expired;
      return;
    }

    try
    {
      SpeckleUrlModelResource? urlModelResource = sendComponent.UrlModelResource;
      if (urlModelResource is null)
      {
        throw new InvalidOperationException("Url Resource was null");
      }

      SpeckleCollectionWrapperGoo? rootCollection = sendComponent.RootCollection;
      if (rootCollection is null)
      {
        throw new InvalidOperationException("Root Collection was null");
      }

      var t = Task.Run(async () =>
      {
        if (CancellationToken.IsCancellationRequested)
        {
          sendComponent.CurrentComponentState = ComponentState.Expired;
          return;
        }

        // Step 1 - SEND TO SERVER
        var sendInfo = await urlModelResource
          .GetSendInfo(sendComponent.ApiClient, CancellationToken)
          .ConfigureAwait(false);

        var progress = new Progress<CardProgress>(p =>
        {
          reportProgress(Id, p.Progress ?? 0);
          //sendComponent.Message = $"{p.Status}";
        });

        var result = await sendComponent
          .SendOperation.Execute(
            new List<SpeckleCollectionWrapperGoo>() { rootCollection },
            sendInfo,
            progress,
            CancellationToken
          )
          .ConfigureAwait(false);

        // TODO: need the created version id here from the send result, not the rootobj id
        SpeckleUrlModelVersionResource? createdVersion =
          new(sendInfo.ServerUrl.ToString(), sendInfo.ProjectId, sendInfo.ModelId, result.RootObjId);
        OutputParam = createdVersion;
        sendComponent.Url = $"{createdVersion.Server}projects/{sendInfo.ProjectId}/models/{sendInfo.ModelId}"; // TODO: missing "@VersionId"

        // DONE
        done();
      });
      t.Wait();
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      RuntimeMessages.Add((GH_RuntimeMessageLevel.Error, ex.ToFormattedString()));
      done();
    }
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
        var autoSendButton = GH_Capsule.CreateTextCapsule(
          ButtonBounds,
          ButtonBounds,
          GH_Palette.Blue,
          "Auto Send",
          2,
          0
        );

        autoSendButton.Render(graphics, Selected, Owner.Locked, false);
        autoSendButton.Dispose();
      }
      else
      {
        var palette =
          state == ComponentState.Expired || state == ComponentState.UpToDate
            ? GH_Palette.Black
            : GH_Palette.Transparent;

        var text = state == ComponentState.Sending ? "Sending..." : "Send";

        var button = GH_Capsule.CreateTextCapsule(
          ButtonBounds,
          ButtonBounds,
          palette,
          text,
          2,
          state == ComponentState.Expired ? 10 : 0
        );
        button.Render(graphics, Selected, Owner.Locked, false);
        button.Dispose();
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
