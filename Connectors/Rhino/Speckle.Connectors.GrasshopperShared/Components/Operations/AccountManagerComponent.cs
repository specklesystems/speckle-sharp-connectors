using Grasshopper.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Rhino;
using Speckle.Connectors.DUI.Settings;
using Speckle.Connectors.GrasshopperShared.Properties;
using Speckle.Connectors.GrasshopperShared.Registration;
using Speckle.Sdk;
using Speckle.Sdk.Credentials;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations;

public class AccountManagerComponent : GH_Component, IDisposable
{
  // matches the DUI connectors, see AccountBinding.AuthenticateAccount
  private static readonly TimeSpan s_authTimeout = TimeSpan.FromMinutes(5);

  private bool _disposed;

  // written by the auth task, read on the UI thread
  private volatile bool _isAddingAccount;
  private CancellationTokenSource? _authCancellation;

  private List<Account>? Accounts { get; set; }
  private string? CustomUrlInput { get; set; }
  private readonly IAccountManager _accountManager;
  private readonly IGlobalConfigResolver _globalConfigResolver;
  public override Guid ComponentGuid => new("c8ede281-acdf-49bf-8611-e9579be1bd41");

  protected override Bitmap Icon => Resources.speckle_operations_account;

  public override GH_Exposure Exposure => GH_Exposure.primary;

  public GhContextMenuButton SignInButton { get; }

  public AccountManagerComponent()
    : base(
      "Sign In",
      "SI",
      "Sign in to a Speckle Account",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.OPERATIONS
    )
  {
    _accountManager = PriorityLoader.Container.GetRequiredService<IAccountManager>();
    _globalConfigResolver = PriorityLoader.Container.GetRequiredService<IGlobalConfigResolver>();
    Accounts = _accountManager.GetAccounts().ToList();

    SignInButton = new GhContextMenuButton("Sign In", "Sign In", "Click to sign into Speckle account.", AuthFlow);
  }

  public override void CreateAttributes() => m_attributes = new AccountManagerComponentAttributes(this);

  private bool AuthFlow(ToolStripDropDown menu)
  {
    if (_isAddingAccount)
    {
      return false;
    }

    Uri serverUrl;
    try
    {
      serverUrl = string.IsNullOrEmpty(CustomUrlInput)
        ? _globalConfigResolver.GetDefaultSpeckleServerUrl()
        : new Uri(new Uri(CustomUrlInput).GetLeftPart(UriPartial.Authority), UriKind.Absolute);
    }
    catch (UriFormatException)
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"'{CustomUrlInput}' is not a valid server url.");
      return false;
    }

    _isAddingAccount = true;
    _authCancellation?.Cancel();
    _authCancellation?.Dispose();
    _authCancellation = new CancellationTokenSource();
    var cancellationToken = _authCancellation.Token;

    // NOTE: fire and forget. AuthenticateAccount opens the browser and blocks until the user is done with it, so
    // awaiting on the UI thread would freeze the canvas for as long as the sign in takes. Task.Run keeps the whole
    // flow (and its continuations) off the UI thread; Complete marshals back.
    _ = Task.Run(() => Authenticate(serverUrl, cancellationToken));
    return true;
  }

  private async Task Authenticate(Uri serverUrl, CancellationToken cancellationToken)
  {
    try
    {
      await _accountManager.AuthenticateAccount(serverUrl, s_authTimeout, cancellationToken).ConfigureAwait(false);
      Complete(GH_RuntimeMessageLevel.Remark, "Account added successfully!");
    }
    catch (OperationCanceledException)
    {
      // superseded by another sign in, or the component went away
      _isAddingAccount = false;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      Complete(GH_RuntimeMessageLevel.Warning, $"Sign in to {serverUrl} failed: {ex.Message}");
    }
  }

  private void Complete(GH_RuntimeMessageLevel level, string message)
  {
    _isAddingAccount = false;
    Accounts = _accountManager.GetAccounts().ToList();

    RhinoApp.InvokeOnUiThread(() =>
    {
      OnPingDocument()
        ?.ScheduleSolution(
          100,
          _ =>
          {
            ExpireSolution(true);
            AddRuntimeMessage(level, message);
          }
        );
    });
  }

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    var urlIndex = pManager.AddTextParameter(
      "Server Url",
      "Url",
      "Optional URL for signing into a self deployed Speckle server.",
      GH_ParamAccess.item
    );
    pManager[urlIndex].Optional = true;
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    pManager.AddTextParameter($"Accounts", "Accounts", "List of available accounts", GH_ParamAccess.list);
  }

  protected override void SolveInstance(IGH_DataAccess da)
  {
    string? urlInput = null;
    if (da.GetData(0, ref urlInput))
    {
      CustomUrlInput = urlInput;
    }

    if (Accounts != null)
    {
      da.SetDataList(0, Accounts);
    }
    else
    {
      da.SetDataList(0, new List<Account>());
    }
  }

  public override void RemovedFromDocument(GH_Document document)
  {
    // NOTE: GH doesn't call Dispose on delete, so without this an abandoned sign in keeps the SDK's callback
    // listener bound until the timeout. Not disposed, the component can come back on undo.
    _authCancellation?.Cancel();
    base.RemovedFromDocument(document);
  }

  public override void ExpirePreview(bool redraw)
  {
    SignInButton.ExpirePreview(redraw);
    base.ExpirePreview(redraw);
  }

  protected virtual void Dispose(bool disposing)
  {
    if (!_disposed)
    {
      if (disposing)
      {
        _authCancellation?.Cancel();
        _authCancellation?.Dispose();
        _accountManager.Dispose();
      }
      _disposed = true;
    }
  }

  public void Dispose()
  {
    Dispose(true);
    GC.SuppressFinalize(this);
  }
}
