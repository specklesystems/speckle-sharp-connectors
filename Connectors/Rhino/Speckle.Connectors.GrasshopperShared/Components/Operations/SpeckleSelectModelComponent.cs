using GH_IO.Serialization;
using Grasshopper.Kernel;
using Speckle.Connectors.GrasshopperShared.Components.Operations.Wizard;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;
using Speckle.Sdk;
using Speckle.Sdk.Credentials;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations;

public class SpeckleSelectModelComponent : GH_Component
{
  private bool _justPastedIn;

  private string? _storedUserId;
  private string? _storedServer;
  private string? _storedWorkspaceId;
  private string? _storedProjectId;
  private string? _storedModelId;
  private string? _storedVersionId;

  public override Guid ComponentGuid => new("9638B3B5-C469-4570-B69F-686D8DA5C48D");

  public GhContextMenuButton WorkspaceContextMenuButton { get; }
  public GhContextMenuButton ProjectContextMenuButton { get; }
  public GhContextMenuButton ModelContextMenuButton { get; }
  public GhContextMenuButton VersionContextMenuButton { get; }

  private SpeckleOperationWizard SpeckleOperationWizard { get; }

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
    SpeckleOperationWizard = new SpeckleOperationWizard(RefreshComponent, UpdateComponentMessage, false);

    WorkspaceContextMenuButton = SpeckleOperationWizard.WorkspaceMenuHandler.WorkspaceContextMenuButton;
    ProjectContextMenuButton = SpeckleOperationWizard.ProjectMenuHandler.ProjectContextMenuButton;
    ModelContextMenuButton = SpeckleOperationWizard.ModelMenuHandler.ModelContextMenuButton;
    VersionContextMenuButton = SpeckleOperationWizard!.VersionMenuHandler!.VersionContextMenuButton; // TODO: fix this shit later when we split
  }

  private Task RefreshComponent()
  {
    ExpireSolution(true);
    return Task.CompletedTask;
  }

  private Task UpdateComponentMessage(string message)
  {
    Message = message;
    return Task.CompletedTask;
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

  private string? UrlInput { get; set; }

#pragma warning disable CA1502
  protected override void SolveInstance(IGH_DataAccess da)
#pragma warning restore CA1502
  {
    try
    {
      // Deal with inputs
      string? urlInput = null;

      // SCENARIO 1: Component has input wire connected
      if (da.GetData(0, ref urlInput))
      {
        UrlInput = urlInput;
        //Lock button interactions before anything else, to ensure any input (even invalid ones) lock the state.
        SpeckleOperationWizard.SetComponentButtonsState(false);

        if (urlInput == null || string.IsNullOrEmpty(urlInput))
        {
          AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input url was empty or null");
          return;
        }

        try
        {
          // NOTE: once we split the logic in Sender and Receiver components, we need to set flag correctly
          var (resource, hasPermission) = SpeckleOperationWizard.SolveInstanceWithUrlInput(urlInput, true);
          if (!hasPermission)
          {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "You do not have enough permission for this project.");
          }
          _storedUserId = SpeckleOperationWizard.SelectedAccount?.id;
          _storedServer = resource.Server;
          da.SetData(0, resource);
        }
        catch (SpeckleException e)
        {
          AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message);
          da.AbortComponentSolution();
        }
        return; // Fast exit!
      }

      // SCENARIO 2: Component is running with no wires connected to input.

      // Unlock button interactions when no input data is provided (no wires connected)
      SpeckleOperationWizard.SetComponentButtonsState(true);

      // When user unplugs the URL input, we need to reset all first
      if (UrlInput != null)
      {
        UrlInput = null;
        SpeckleOperationWizard.ResetHandlers();
        _storedUserId = SpeckleOperationWizard.SelectedAccount?.id;
      }

      if (_justPastedIn && _storedUserId != null && !string.IsNullOrEmpty(_storedUserId))
      {
        try
        {
          SpeckleOperationWizard.SetAccountFromId(_storedUserId);
        }
        catch (SpeckleAccountManagerException e)
        {
          // Swallow and move onto checking server.
          Console.WriteLine(e);
        }

        if (_storedServer != null && SpeckleOperationWizard.SelectedAccount == null)
        {
          SpeckleOperationWizard.SetAccountFromIdAndUrl(_storedUserId, _storedServer);
        }
      }

      // Validate backing data
      if (SpeckleOperationWizard.SelectedAccount == null)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Please select an account in the right click menu");
        WorkspaceContextMenuButton.Enabled = false;
        ProjectContextMenuButton.Enabled = false;
        ModelContextMenuButton.Enabled = false;
        VersionContextMenuButton.Enabled = false;
        da.AbortComponentSolution();
        return;
      }

      // 1- Workspaces

      if (_justPastedIn && !string.IsNullOrEmpty(_storedWorkspaceId))
      {
        SpeckleOperationWizard.SetWorkspaceFromSavedIdSync(_storedWorkspaceId!);
      }

      // NOTE FOR LATER: Need to be handled in SDK... and will come later by Jeddward Morgan...
      if (SpeckleOperationWizard.WorkspaceMenuHandler.Workspaces == null)
      {
        var workspaces = SpeckleOperationWizard.FetchWorkspacesSync("");
        if (workspaces.items.Count == 0)
        {
          // Create a workspace flow
          SpeckleOperationWizard.CreateNewWorkspaceUIState();
          da.AbortComponentSolution();
          return;
        }
      }

      // Unfortunately need to handle personal projects as workspace item for a while
      if (SpeckleOperationWizard.WorkspaceMenuHandler.IsPersonalProjects)
      {
        _storedWorkspaceId = null;
      }
      else
      {
        SpeckleOperationWizard.SetDefaultWorkspaceSync();

        if (SpeckleOperationWizard.SelectedWorkspace != null)
        {
          _storedWorkspaceId = SpeckleOperationWizard.SelectedWorkspace.id;
        }
        else
        {
          da.AbortComponentSolution();
          return;
        }
      }

      // 2- Projects
      ProjectContextMenuButton.Enabled = true;

      // Get projects for menu
      SpeckleOperationWizard.FetchProjectsSync("");

      // Stored project id scenario
      if (_justPastedIn && !string.IsNullOrEmpty(_storedProjectId))
      {
        SpeckleOperationWizard.SetProjectFromSavedIdSync(_storedProjectId!);
      }

      // No selected project scenario
      if (SpeckleOperationWizard.SelectedProject == null)
      {
        ModelContextMenuButton.Enabled = false;
        VersionContextMenuButton.Enabled = false;

        da.AbortComponentSolution();
        return;
      }

      // 3- Models

      ModelContextMenuButton.Enabled = true;

      // Get models for menu
      SpeckleOperationWizard.FetchModelsSync("");

      if (_justPastedIn && !string.IsNullOrEmpty(_storedModelId))
      {
        SpeckleOperationWizard.SetModelFromSavedIdSync(_storedModelId!);
      }

      // No selected model scenario
      if (SpeckleOperationWizard.SelectedModel == null)
      {
        VersionContextMenuButton.Enabled = false;
        da.AbortComponentSolution();
        return;
      }

      // 4- Models

      VersionContextMenuButton.Enabled = true;
      SpeckleOperationWizard.FetchVersionsSync(10);

      if (_justPastedIn && !string.IsNullOrEmpty(_storedVersionId))
      {
        SpeckleOperationWizard.SetVersionFromSavedIdSync(_storedVersionId!);
      }

      if (SpeckleOperationWizard.SelectedVersion == null)
      {
        // If no version selected, output `latest` resource
        da.SetData(
          0,
          new SpeckleUrlLatestModelVersionResource(
            SpeckleOperationWizard.SelectedAccount.id,
            SpeckleOperationWizard.SelectedAccount.serverInfo.url,
            SpeckleOperationWizard.SelectedWorkspace?.id,
            SpeckleOperationWizard.SelectedProject.id,
            SpeckleOperationWizard.SelectedModel.id
          )
        );
        return;
      }

      // If all data points are selected, output specific version.
      da.SetData(
        0,
        new SpeckleUrlModelVersionResource(
          SpeckleOperationWizard.SelectedAccount.id,
          SpeckleOperationWizard.SelectedAccount.serverInfo.url,
          SpeckleOperationWizard.SelectedWorkspace?.id,
          SpeckleOperationWizard.SelectedProject.id,
          SpeckleOperationWizard.SelectedModel.id,
          SpeckleOperationWizard.SelectedVersion.id
        )
      );
    }
    catch (AggregateException e) when (!e.IsFatal())
    {
      AddRuntimeMessage(
        GH_RuntimeMessageLevel.Error,
        string.Join("\n", e.InnerExceptions.Select(innerE => innerE.Message))
      );
      da.AbortComponentSolution();
    }
    catch (Exception e) when (!e.IsFatal())
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message);
      da.AbortComponentSolution();
    }
  }

  protected override void AfterSolveInstance()
  {
    // If the component runs once till the end, then it's no longer "just pasted in".
    _justPastedIn = false;
    base.AfterSolveInstance();
  }

  public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
  {
    base.AppendAdditionalMenuItems(menu);
    var accountsMenu = Menu_AppendItem(menu, "Account");

    if (SpeckleOperationWizard.Accounts == null)
    {
      // TODO: we have to think about auth flow!
      AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Please add an account");
      return;
    }

    foreach (var account in SpeckleOperationWizard.Accounts)
    {
      Menu_AppendItem(
        accountsMenu.DropDown,
        account.ToString(),
        (_, _) => SpeckleOperationWizard.SetAccount(account),
        null,
        SpeckleOperationWizard.SelectedAccount?.id != account.id,
        SpeckleOperationWizard.SelectedAccount?.id == account.id
      );
    }
  }

  public override bool Write(GH_IWriter writer)
  {
    var baseRes = base.Write(writer);
    writer.SetString("Server", SpeckleOperationWizard.SelectedAccount?.serverInfo.url);
    writer.SetString("User", SpeckleOperationWizard.SelectedAccount?.id);
    writer.SetString("Workspace", SpeckleOperationWizard.SelectedWorkspace?.id);
    writer.SetString("Project", SpeckleOperationWizard.SelectedProject?.id);
    writer.SetString("Model", SpeckleOperationWizard.SelectedModel?.id);
    writer.SetString("Version", SpeckleOperationWizard.SelectedVersion?.id);

    return baseRes;
  }

  public override bool Read(GH_IReader reader)
  {
    var readRes = base.Read(reader);

    reader.TryGetString("Server", ref _storedServer);
    reader.TryGetString("User", ref _storedUserId);
    reader.TryGetString("Workspace", ref _storedWorkspaceId);
    reader.TryGetString("Project", ref _storedProjectId);
    reader.TryGetString("Model", ref _storedModelId);
    reader.TryGetString("Version", ref _storedVersionId);

    _justPastedIn = true;
    return readRes;
  }

  public override void ExpirePreview(bool redraw)
  {
    WorkspaceContextMenuButton.ExpirePreview(redraw);
    ProjectContextMenuButton.ExpirePreview(redraw);
    ModelContextMenuButton.ExpirePreview(redraw);
    VersionContextMenuButton.ExpirePreview(redraw);
    base.ExpirePreview(redraw);
  }
}
