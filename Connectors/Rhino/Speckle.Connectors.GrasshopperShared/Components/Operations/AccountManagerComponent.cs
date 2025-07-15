using System.Diagnostics;
using Grasshopper.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.GrasshopperShared.Properties;
using Speckle.Connectors.GrasshopperShared.Registration;
using Speckle.Sdk;
using Speckle.Sdk.Credentials;
using Timer = System.Timers.Timer;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations;

public class AccountManagerComponent : GH_Component, IDisposable
{
  private bool _disposed;
  private bool _isAddingAccount;
  private Timer _timeoutTimer;
  private Timer? _accountCheckerTimer;

  private List<Account>? Accounts { get; set; }
  private string? CustomUrlInput { get; set; }
  private readonly IAccountManager _accountManager;
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
    Attributes = new AccountManagerComponentAttributes(this);
    _accountManager = PriorityLoader.Container.GetRequiredService<IAccountManager>();
    Accounts = _accountManager.GetAccounts().ToList();

    SignInButton = new GhContextMenuButton("Sign In", "Sign In", "Click to sign into Speckle account.", AuthFlow);
  }

  private bool AuthFlow(ToolStripDropDown menu)
  {
    _isAddingAccount = true;
    ResumeAccountChecker();

    string url = string.IsNullOrEmpty(CustomUrlInput)
      ? "http://localhost:29364/auth/add-account"
      : $"http://localhost:29364/auth/add-account?serverUrl={new Uri(CustomUrlInput).GetLeftPart(UriPartial.Authority)}";

    // Open the auth URL in the default browser
    try
    {
      Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Sign in failed: {ex.Message}");
      _isAddingAccount = false;
      return false;
    }

    _timeoutTimer = new Timer(30_000);
    _timeoutTimer.Elapsed += (s, e) =>
    {
      _timeoutTimer.Stop();
      if (_isAddingAccount)
      {
        _isAddingAccount = false;
        PauseAccountChecker();
        AddRuntimeMessage(
          GH_RuntimeMessageLevel.Warning,
          "Sign in timed out. This may have happened because you tried adding an existing account."
        );
      }
    };

    _timeoutTimer.Start();
    return true;
  }

  private bool CheckIfAccountAdded()
  {
    var previousAccountCount = Accounts?.Count ?? 0;
    Accounts = _accountManager.GetAccounts().ToList();
    return previousAccountCount < Accounts.Count;
  }

  private void PauseAccountChecker()
  {
    _accountCheckerTimer?.Stop();
    _accountCheckerTimer?.Dispose();
    _accountCheckerTimer = null;
  }

  private void ResumeAccountChecker()
  {
    _accountCheckerTimer?.Dispose();

    _accountCheckerTimer = new Timer(1000); // check every 1 second
    _accountCheckerTimer.Elapsed += (s, e) =>
    {
      bool accountAdded = CheckIfAccountAdded();
      if (accountAdded)
      {
        _accountCheckerTimer.Stop();
        _isAddingAccount = false;
        // Optionally cancel timeout timer
        _timeoutTimer?.Stop();

        OnPingDocument()
          ?.ScheduleSolution(
            100,
            doc =>
            {
              ExpireSolution(true);
              AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Account added successfully!");
            }
          );
      }
    };
    _accountCheckerTimer.Start();
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
        _timeoutTimer?.Dispose();
        _accountManager.Dispose();
        _accountCheckerTimer?.Dispose();
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
