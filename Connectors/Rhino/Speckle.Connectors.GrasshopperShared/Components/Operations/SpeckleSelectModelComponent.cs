using GH_IO.Serialization;
using Grasshopper.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Registration;
using Speckle.Sdk;
using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Models;
using Speckle.Sdk.Credentials;
using Version = Speckle.Sdk.Api.GraphQL.Models.Version;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations;

public class SpeckleSelectModelComponent : GH_Component
{
  private Project? _project;
  private Model? _model;
  private Version? _version;
  private Account? _account;

  private bool _justPastedIn;

  private string? _storedUserId;
  private string? _storedServer;
  private string? _storedProjectId;
  private string? _storedModelId;
  private string? _storedVersionId;

  private readonly AccountService _accountService;
  private readonly AccountManager _accountManager;
  private readonly IClientFactory _clientFactory;

  public ResourceCollection<Project>? LastFetchedProjects { get; set; }
  public ResourceCollection<Model>? LastFetchedModels { get; set; }
  public ResourceCollection<Version>? LastFetchedVersions { get; set; }

  public GhContextMenuButton ProjectContextMenuButton { get; set; }
  public GhContextMenuButton ModelContextMenuButton { get; set; }
  public GhContextMenuButton VersionContextMenuButton { get; set; }

  protected override Bitmap Icon => BitmapBuilder.CreateSquareIconBitmap("URL");

  public SpeckleSelectModelComponent()
    : base(
      "Speckle Model URL",
      "URL",
      "User selectable model from Speckle",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.OPERATIONS
    )
  {
    ProjectContextMenuButton = new GhContextMenuButton(
      "Select Project",
      "Select Project",
      "Right-click to select project",
      PopulateProjectMenu
    );
    ModelContextMenuButton = new GhContextMenuButton(
      "Select Model",
      "Select Project",
      "Right-click to select a model",
      PopulateModelMenu
    );
    VersionContextMenuButton = new GhContextMenuButton(
      "Select Version",
      "Select Version",
      "Right-click to select a version",
      PopulateVersionMenu
    );

    Attributes = new SpeckleSelectModelComponentAttributes(this);
    _accountService = PriorityLoader.Container.GetRequiredService<AccountService>();
    _accountManager = PriorityLoader.Container.GetRequiredService<AccountManager>();
    _clientFactory = PriorityLoader.Container.GetRequiredService<IClientFactory>();
    var account = _accountManager.GetDefaultAccount();
    OnAccountSelected(account);
  }

  private bool PopulateVersionMenu(ToolStripDropDown menu)
  {
    if (LastFetchedVersions is null)
    {
      Menu_AppendItem(menu, "No versions were fetched");
      return true;
    }

    if (LastFetchedVersions.items.Count == 0)
    {
      Menu_AppendItem(menu, "Model has no versions");
      return true;
    }

    Menu_AppendItem(menu, "Search...", null, null, false, false);
    Menu_AppendSeparator(menu);
    Menu_AppendItem(
      menu,
      "Latest Version",
      (_, _) => OnVersionSelected(null),
      null,
      _version != null,
      _version == null
    );

    foreach (var version in LastFetchedVersions.items)
    {
      var desc = string.IsNullOrEmpty(version.message) ? "No description" : version.message;

      Menu_AppendItem(
        menu,
        $"{version.id} - {desc}",
        (_, _) => OnVersionSelected(version),
        null,
        _version?.id != version.id,
        _version?.id == version.id
      );
    }

    return true;
  }

  private bool PopulateModelMenu(ToolStripDropDown menu)
  {
    if (LastFetchedModels == null)
    {
      Menu_AppendItem(menu, "No models were fetched");
      return true;
    }

    if (LastFetchedModels.items.Count == 0)
    {
      Menu_AppendItem(menu, "Project has no models");
      return true;
    }

    Menu_AppendItem(menu, "Search...", null, null, false, false);
    Menu_AppendSeparator(menu);

    foreach (var model in LastFetchedModels.items)
    {
      var desc = string.IsNullOrEmpty(model.description) ? "No description" : model.description;

      Menu_AppendItem(
        menu,
        $"{model.name} - {desc}",
        (_, _) => OnModelSelected(model),
        null,
        _model?.id != model.id,
        _model?.id == model.id
      );
    }

    return true;
  }

  private bool PopulateProjectMenu(ToolStripDropDown menu)
  {
    if (LastFetchedProjects == null)
    {
      Menu_AppendItem(menu, "No projects were fetched");
      return true;
    }

    Menu_AppendItem(menu, "Search...", null, null, false, false);
    Menu_AppendSeparator(menu);

    foreach (var project in LastFetchedProjects.items)
    {
      var desc = string.IsNullOrEmpty(project.description) ? "No description" : project.description;
      Menu_AppendItem(
        menu,
        $"{project.name} - {desc}",
        (_, _) => OnProjectSelected(project),
        _project?.id != project.id,
        _project?.id == project.id
      );
    }

    return true;
  }

  private void OnAccountSelected(Account? account, bool expire = true, bool redraw = true)
  {
    _account = account;
    Message = _account != null ? $"{_account.serverInfo.url}\n{_account.userInfo.email}" : null;
    LastFetchedProjects = null;
    OnProjectSelected(null, expire, redraw);
  }

  private void OnProjectSelected(Project? project, bool expire = true, bool redraw = true)
  {
    _project = project;
    var suffix = ProjectContextMenuButton.Enabled
      ? "Right-click to select another project."
      : "Selection is disabled due to component input.";
    if (_project != null)
    {
      ProjectContextMenuButton.Name = _project.name;
      ProjectContextMenuButton.NickName = _project.id;
      ProjectContextMenuButton.Description = $"{_project.description ?? "No description"}\n\n{suffix}";
    }
    else
    {
      ProjectContextMenuButton.Name = "Select Project";
      ProjectContextMenuButton.NickName = "Project";
      ProjectContextMenuButton.Description = "Right-click to select project";
    }
    LastFetchedModels = null;
    OnModelSelected(null, expire, redraw);
  }

  private void OnModelSelected(Model? model, bool expire = true, bool redraw = true)
  {
    _model = model;
    var suffix = ModelContextMenuButton.Enabled
      ? "Right-click to select another model."
      : "Selection is disabled due to component input.";
    if (_model != null)
    {
      ModelContextMenuButton.Name = _model.name;
      ModelContextMenuButton.NickName = _model.id;
      ModelContextMenuButton.Description = $"{_model.description ?? "No description"}\n\n{suffix}";
    }
    else
    {
      ModelContextMenuButton.Name = "Select Model";
      ModelContextMenuButton.NickName = "Model";
      ModelContextMenuButton.Description = "Right-click to select model";
    }
    LastFetchedVersions = null;
    OnVersionSelected(null, expire, redraw);
  }

  private void OnVersionSelected(Version? version, bool expire = true, bool redraw = true)
  {
    _version = version;
    var suffix = VersionContextMenuButton.Enabled
      ? "Right-click to select another version."
      : "Selection is disabled due to component input.";
    if (_version != null)
    {
      VersionContextMenuButton.Name = _version.id;
      VersionContextMenuButton.NickName = _version.id;
      VersionContextMenuButton.Description = $"{_version.message ?? "No message"}\n\n{suffix}";
    }
    else if (_model != null)
    {
      VersionContextMenuButton.NickName = "Latest Version";
      VersionContextMenuButton.Name = "Latest Version";
      VersionContextMenuButton.Description = "Gets the latest version from the selected model";
    }
    else
    {
      VersionContextMenuButton.Name = "Select Version";
      VersionContextMenuButton.NickName = "Version";
      VersionContextMenuButton.Description = "Right-click to select version";
    }
    if (expire)
    {
      ExpirePreview(redraw);
      ExpireSolution(true);
    }
  }

  public override Guid ComponentGuid => new("9638B3B5-C469-4570-B69F-686D8DA5C48D");

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    var urlIndex = pManager.AddTextParameter("Speckle Url", "Url", "Speckle URL", GH_ParamAccess.item);
    pManager[urlIndex].Optional = true;
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    pManager.AddParameter(new SpeckleUrlModelResourceParam());
  }

  protected override void SolveInstance(IGH_DataAccess da)
  {
    // Deal with inputs
    string? urlInput = null;

    // OPTION 1: Component has input wire connected
    if (da.GetData(0, ref urlInput))
    {
      //Lock button interactions before anything else, to ensure any input (even invalid ones) lock the state.
      SetComponentButtonsState(false);

      if (urlInput == null || string.IsNullOrEmpty(urlInput))
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input url was empty or null");
        return;
      }

      try
      {
        var resource = SolveInstanceWithUrlInput(urlInput);
        da.SetData(0, resource);
      }
      catch (SpeckleException e)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message);
      }
      return; // Fast exit!
    }

    // OPTION 2: Component is running with no wires connected to input.

    // Unlock button interactions when no input data is provided (no wires connected)
    SetComponentButtonsState(true);

    if (_justPastedIn && _storedUserId != null && !string.IsNullOrEmpty(_storedUserId))
    {
      try
      {
        var account = _accountManager.GetAccount(_storedUserId);
        OnAccountSelected(account, false);
      }
      catch (SpeckleAccountManagerException e)
      {
        // Swallow and move onto checking server.
        Console.WriteLine(e);
      }

      if (_storedServer != null && _account == null)
      {
        var account = _accountService.GetAccountWithServerUrlFallback(_storedUserId ?? "", new Uri(_storedServer));
        OnAccountSelected(account, false);
      }
    }

    // Validate backing data
    if (_account == null)
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Please select an account in the right click menu");
      ProjectContextMenuButton.Enabled = false;
      ModelContextMenuButton.Enabled = false;
      VersionContextMenuButton.Enabled = false;
      return;
    }

    Client client = _clientFactory.Create(_account);

    LastFetchedProjects = client.ActiveUser.GetProjects(10, null, null).Result;
    ProjectContextMenuButton.Enabled = true;

    if (_justPastedIn && !string.IsNullOrEmpty(_storedProjectId))
    {
      var project = client.Project.Get(_storedProjectId!).Result;
      OnProjectSelected(project, false);
    }

    if (_project == null)
    {
      ModelContextMenuButton.Enabled = false;
      VersionContextMenuButton.Enabled = false;
      return;
    }

    LastFetchedModels = client.Project.GetWithModels(_project.id, 10).Result.models;
    ModelContextMenuButton.Enabled = true;

    if (_justPastedIn && !string.IsNullOrEmpty(_storedModelId))
    {
      var model = client.Model.Get(_storedModelId!, _project.id).Result;
      OnModelSelected(model, false);
    }

    if (_model == null)
    {
      VersionContextMenuButton.Enabled = false;
      return;
    }

    LastFetchedVersions = client.Model.GetWithVersions(_model.id, _project.id, 10).Result.versions;
    VersionContextMenuButton.Enabled = true;

    if (_justPastedIn && !string.IsNullOrEmpty(_storedVersionId))
    {
      var version = client.Version.Get(_storedVersionId!, _project.id).Result;
      OnVersionSelected(version);
    }
    if (_version == null)
    {
      // If no version selected, output `latest` resource
      da.SetData(0, new SpeckleUrlLatestModelVersionResource(_account.serverInfo.url, _project.id, _model.id));
      return;
    }

    // If all data points are selected, output specific version.
    da.SetData(0, new SpeckleUrlModelVersionResource(_account.serverInfo.url, _project.id, _model.id, _version.id));
  }

  protected override void AfterSolveInstance()
  {
    // If the component runs once till the end, then it's no longer "just pasted in".
    _justPastedIn = false;
    base.AfterSolveInstance();
  }

  private void SetComponentButtonsState(bool enabled)
  {
    ProjectContextMenuButton.Enabled = enabled;
    ModelContextMenuButton.Enabled = enabled;
    VersionContextMenuButton.Enabled = enabled;
  }

  private SpeckleUrlModelResource SolveInstanceWithUrlInput(string urlInput)
  {
    // When input is provided, lock interaction of buttons so only text is shown (no context menu)
    // Should perform validation, fill in all internal data of the component (project, model, version, account)
    // Should notify user if any of this goes wrong.

    var resources = SpeckleResourceBuilder.FromUrlString(urlInput);
    if (resources.Length == 0)
    {
      throw new SpeckleException($"Input url string was empty");
    }

    if (resources.Length > 1)
    {
      throw new SpeckleException($"Input multi-model url is not supported");
    }

    var resource = resources.First();

    var account = _accountService.GetAccountWithServerUrlFallback(string.Empty, new Uri(resource.Server));
    OnAccountSelected(account, false);

    if (_account == null)
    {
      throw new SpeckleException("No account found for server URL");
    }

    Client client = _clientFactory.Create(_account);

    var project = client.Project.Get(resource.ProjectId).Result;
    OnProjectSelected(project, false);

    switch (resource)
    {
      case SpeckleUrlLatestModelVersionResource latestVersionResource:
        var model = client.Model.Get(latestVersionResource.ModelId, latestVersionResource.ProjectId).Result;
        OnModelSelected(model, false);
        break;
      case SpeckleUrlModelVersionResource versionResource:
        var m = client.Model.Get(versionResource.ModelId, versionResource.ProjectId).Result;
        OnModelSelected(m, false);
        var v = client.Version.Get(versionResource.VersionId, versionResource.ProjectId).Result;
        OnVersionSelected(v, false);
        break;
      case SpeckleUrlModelObjectResource:
        throw new SpeckleException("Object URLs are not supported");
      default:
        throw new SpeckleException("Unknown Speckle resource type");
    }

    return resource;
  }

  public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
  {
    base.AppendAdditionalMenuItems(menu);
    var accountsMenu = Menu_AppendItem(menu, "Account");

    foreach (var account in _accountManager.GetAccounts())
    {
      Menu_AppendItem(
        accountsMenu.DropDown,
        account.ToString(),
        (_, _) => OnAccountSelected(account),
        null,
        _account?.id != account.id,
        _account?.id == account.id
      );
    }
  }

  public override bool Write(GH_IWriter writer)
  {
    var baseRes = base.Write(writer);
    writer.SetString("Server", _account?.serverInfo.url);
    writer.SetString("User", _account?.id);
    writer.SetString("Project", _project?.id);
    writer.SetString("Model", _model?.id);
    writer.SetString("Version", _version?.id);

    return baseRes;
  }

  public override bool Read(GH_IReader reader)
  {
    var readRes = base.Read(reader);

    reader.TryGetString("Server", ref _storedServer);
    reader.TryGetString("User", ref _storedUserId);
    reader.TryGetString("Project", ref _storedProjectId);
    reader.TryGetString("Model", ref _storedModelId);
    reader.TryGetString("Version", ref _storedVersionId);

    _justPastedIn = true;
    return readRes;
  }

  public override void ExpirePreview(bool redraw)
  {
    ProjectContextMenuButton.ExpirePreview(redraw);
    ModelContextMenuButton.ExpirePreview(redraw);
    VersionContextMenuButton.ExpirePreview(redraw);
    base.ExpirePreview(redraw);
  }
}
