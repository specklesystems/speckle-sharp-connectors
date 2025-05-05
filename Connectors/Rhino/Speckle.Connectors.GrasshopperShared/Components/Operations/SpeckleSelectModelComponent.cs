using GH_IO.Serialization;
using Grasshopper.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;
using Speckle.Connectors.GrasshopperShared.Registration;
using Speckle.Sdk;
using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Inputs;
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

  private readonly IAccountService _accountService;
  private readonly AccountManager _accountManager;
  private readonly IClientFactory _clientFactory;

  public override Guid ComponentGuid => new("9638B3B5-C469-4570-B69F-686D8DA5C48D");

  private ResourceCollection<Project>? LastFetchedProjects { get; set; }
  private ResourceCollection<Model>? LastFetchedModels { get; set; }
  private ResourceCollection<Version>? LastFetchedVersions { get; set; }

  private int FetchedVersionCount { get; set; } = 10;

  private readonly ProjectMenuHandler _projectMenuHandler;
  private readonly ModelMenuHandler _modelMenuHandler;

  public GhContextMenuButton ProjectContextMenuButton { get; set; }
  public GhContextMenuButton ModelContextMenuButton { get; set; }
  public GhContextMenuButton VersionContextMenuButton { get; set; }
  private ToolStripDropDown? VersionDropDown { get; set; }

  protected override Bitmap Icon => Resources.speckle_inputs_model;

  public SpeckleSelectModelComponent()
    : base(
      "Speckle Model URL",
      "URL",
      "User selectable model from Speckle",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.OPERATIONS
    )
  {
    Attributes = new SpeckleSelectModelComponentAttributes(this);
    _accountService = PriorityLoader.Container.GetRequiredService<IAccountService>();
    _accountManager = PriorityLoader.Container.GetRequiredService<AccountManager>();
    _clientFactory = PriorityLoader.Container.GetRequiredService<IClientFactory>();
    var account = _accountManager.GetDefaultAccount();
    OnAccountSelected(account);

    _projectMenuHandler = new ProjectMenuHandler(FetchProjects);
    ProjectContextMenuButton = _projectMenuHandler.ProjectContextMenuButton;

    _modelMenuHandler = new ModelMenuHandler(FetchModels);
    ModelContextMenuButton = _modelMenuHandler.ModelContextMenuButton;

    VersionContextMenuButton = new GhContextMenuButton(
      "Select Version",
      "Select Version",
      "Right-click to select a version",
      PopulateVersionMenu
    );

    _projectMenuHandler.ProjectSelected += OnProjectSelected;
    _modelMenuHandler.ModelSelected += OnModelSelected;
  }

  /// <summary>
  /// Callback function to retrieve projects with the search text
  /// </summary>
  private async Task<ResourceCollection<Project>> FetchProjects(string searchText)
  {
    if (_account == null)
    {
      return new ResourceCollection<Project>();
    }

    IClient client = _clientFactory.Create(_account);
    var projects = await client.ActiveUser.GetProjects(10, null, new UserProjectsFilter(searchText));
    LastFetchedProjects = projects;
    return projects;
  }

  /// <summary>
  /// Callback function to retrieve models with the search text
  /// </summary>
  private async Task<ResourceCollection<Model>> FetchModels(string searchText)
  {
    if (_account == null || _project == null)
    {
      return new ResourceCollection<Model>();
    }

    IClient client = _clientFactory.Create(_account);
    var projectWithModels = await client
      .Project.GetWithModels(_project.id, 10, modelsFilter: new ProjectModelsFilter(search: searchText))
      .ConfigureAwait(true);
    LastFetchedModels = projectWithModels.models;
    return projectWithModels.models;
  }

  private async Task FetchMoreVersions(ToolStripDropDown menu)
  {
    if (_account != null && _model != null && _project != null)
    {
      FetchedVersionCount += 10;
      IClient client = _clientFactory.Create(_account);
      var newVersionsResult = await client
        .Model.GetWithVersions(_model.id, _project.id, FetchedVersionCount)
        .ConfigureAwait(true);
      LastFetchedVersions = newVersionsResult.versions;
      PopulateVersionMenuItems(menu);
    }
  }

  private async void OnProjectSelected(object sender, ProjectSelectedEventArgs e)
  {
    _project = e.SelectedProject;
    if (_account != null && _project != null)
    {
      IClient client = _clientFactory.Create(_account);
      var projectWithModels = await client.Project.GetWithModels(_project.id, 10).ConfigureAwait(true);
      LastFetchedModels = projectWithModels.models;
      _modelMenuHandler.Models = LastFetchedModels;
      _modelMenuHandler.RedrawMenuButton(null);
      ModelContextMenuButton.Enabled = true;
      Attributes.ExpireLayout();
    }
  }

  private async void OnModelSelected(object sender, ModelSelectedEventArgs e)
  {
    _model = e.SelectedModel;
    if (_account != null && _project != null && _model != null)
    {
      IClient client = _clientFactory.Create(_account);
      var modelWithVersions = await client
        .Model.GetWithVersions(_model.id, _project.id, FetchedVersionCount)
        .ConfigureAwait(true);
      LastFetchedVersions = modelWithVersions.versions;
      VersionContextMenuButton.Enabled = true;
    }
  }

  private bool PopulateVersionMenu(ToolStripDropDown menu)
  {
    VersionDropDown = menu;
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

    PopulateVersionMenuItems(menu);

    return true;
  }

  private void PopulateVersionMenuItems(ToolStripDropDown menu)
  {
    menu.Items.Clear();

    if (LastFetchedVersions == null)
    {
      return;
    }

    Menu_AppendItem(menu, "Latest Version", (_, _) => OnVersionSelected(null), null, true, _version == null);
    Menu_AppendSeparator(menu);
    foreach (var version in LastFetchedVersions.items)
    {
      var desc = string.IsNullOrEmpty(version.message) ? "No description" : version.message;

      var versionItem = Menu_AppendItem(
        menu,
        $"{version.id} - {desc}",
        (_, _) => OnVersionSelected(version),
        null,
        true,
        _version?.id == version.id
      );
      if (version.referencedObject is null)
      {
        versionItem.Enabled = false;
        versionItem.ToolTipText = @"Upgrade to load older versions";
      }
    }

    if (LastFetchedVersions.items.Count >= FetchedVersionCount)
    {
      Menu_AppendSeparator(menu);

      var addMoreButton = new Button() { Text = @"➕ Show more...", Size = new Size(400, 48) };

      addMoreButton.Click += async (sender, args) =>
      {
        await FetchMoreVersions(menu);
      };

      var addMoreButtonHost = new ToolStripControlHost(addMoreButton)
      {
        Name = "Show more...",
        AutoSize = false,
        Margin = new Padding(4),
        Padding = new Padding(2)
      };

      menu.Items.Insert(menu.Items.Count, addMoreButtonHost);
    }
  }

  private void OnAccountSelected(Account? account)
  {
    _account = account;
    Message = _account != null ? $"{_account.serverInfo.url}\n{_account.userInfo.email}" : null;
    LastFetchedProjects = null;
    // OnProjectSelected(null, expire, redraw);
  }

  private void OnVersionSelected(Version? version, bool expire = true, bool redraw = true)
  {
    VersionDropDown?.Close();
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
        OnAccountSelected(account);
      }
      catch (SpeckleAccountManagerException e)
      {
        // Swallow and move onto checking server.
        Console.WriteLine(e);
      }

      if (_storedServer != null && _account == null)
      {
        var account = _accountService.GetAccountWithServerUrlFallback(_storedUserId ?? "", new Uri(_storedServer));
        OnAccountSelected(account);
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

    IClient client = _clientFactory.Create(_account);
    LastFetchedProjects = client.ActiveUser.GetProjects(10, null, null).Result;
    _projectMenuHandler.Projects = LastFetchedProjects;

    ProjectContextMenuButton.Enabled = true;

    if (_justPastedIn && !string.IsNullOrEmpty(_storedProjectId))
    {
      var project = client.Project.Get(_storedProjectId!).Result;
      _projectMenuHandler.RedrawMenuButton(project);
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
      _modelMenuHandler.RedrawMenuButton(model);
    }

    if (_model == null)
    {
      VersionContextMenuButton.Enabled = false;
      return;
    }

    LastFetchedVersions = client.Model.GetWithVersions(_model.id, _project.id, FetchedVersionCount).Result.versions;
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
    OnAccountSelected(account);

    if (_account == null)
    {
      throw new SpeckleException("No account found for server URL");
    }

    IClient client = _clientFactory.Create(_account);

    //var project = client.Project.Get(resource.ProjectId).Result;
    //OnProjectSelected(project, false);

    switch (resource)
    {
      case SpeckleUrlLatestModelVersionResource latestVersionResource:
        var model = client.Model.Get(latestVersionResource.ModelId, latestVersionResource.ProjectId).Result;
        _modelMenuHandler.RedrawMenuButton(model);
        break;
      case SpeckleUrlModelVersionResource versionResource:
        var m = client.Model.Get(versionResource.ModelId, versionResource.ProjectId).Result;
        _modelMenuHandler.RedrawMenuButton(m);
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
