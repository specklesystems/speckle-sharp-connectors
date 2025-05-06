using Speckle.Sdk.Api.GraphQL.Models;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations;

public class ModelSelectedEventArgs(Model? model) : EventArgs
{
  public Model? SelectedModel { get; } = model;
}

/// <summary>
/// Helper class to manage model filtering and selection for the components.
/// </summary>
public class ModelMenuHandler
{
  private readonly Func<string, Task<ResourceCollection<Model>>> _fetchModels;
  private ToolStripDropDown? _menu;
  private SearchToolStripMenuItem? _searchItem;
  private Model? SelectedModel { get; set; }

  public ResourceCollection<Model>? Models { get; set; }

  public event EventHandler<ModelSelectedEventArgs>? ModelSelected;

  public GhContextMenuButton ModelContextMenuButton { get; set; }

  public ModelMenuHandler(Func<string, Task<ResourceCollection<Model>>> fetchModels)
  {
    _fetchModels = fetchModels;
    ModelContextMenuButton = new GhContextMenuButton(
      "Select Model",
      "Select Model",
      "Right-click to select a model",
      PopulateMenu
    );
  }

  public void Reset()
  {
    RedrawMenuButton(null);
  }

  private async Task Refetch(string searchText)
  {
    Models = await _fetchModels.Invoke(searchText);
    PopulateMenu(_menu!);
  }

  private bool PopulateMenu(ToolStripDropDown menu)
  {
    _menu = menu;
    _menu.Closed += (_, _) =>
    {
      _searchItem = null;
    };

    if (Models == null)
    {
      _searchItem?.AddMenuItem("No models were fetched");
      return true;
    }

    if (Models.items.Count == 0)
    {
      _searchItem?.AddMenuItem("Project has no models");
      return true;
    }

    PopulateModelMenuItems(menu);

    return true;
  }

  private void PopulateModelMenuItems(ToolStripDropDown menu)
  {
    var lastIndex = menu.Items.Count - 1;
    if (lastIndex >= 0)
    {
      // clean the existing items because we re-populate when user search
      for (int i = lastIndex; i > 1; i--)
      {
        menu.Items.RemoveAt(i);
      }
    }

    if (Models == null)
    {
      return;
    }

    if (_searchItem == null)
    {
      _searchItem = new SearchToolStripMenuItem(menu, Refetch);
      _searchItem.AddMenuSeparator();
    }

    foreach (var model in Models.items)
    {
      var desc = string.IsNullOrEmpty(model.description) ? "No description" : model.description;

      _searchItem?.AddMenuItem(
        $"{model.name} - {desc}",
        (_, _) => OnModelSelected(model),
        SelectedModel?.id != model.id,
        SelectedModel?.id == model.id
      );
    }
  }

  public void RedrawMenuButton(Model? model)
  {
    var suffix = ModelContextMenuButton.Enabled
      ? "Right-click to select another model."
      : "Selection is disabled due to component input.";
    if (model != null)
    {
      ModelContextMenuButton.Name = model.name;
      ModelContextMenuButton.NickName = model.id;
      ModelContextMenuButton.Description = $"{model.description ?? "No description"}\n\n{suffix}";
    }
    else
    {
      ModelContextMenuButton.Name = "Select Model";
      ModelContextMenuButton.NickName = "Model";
      ModelContextMenuButton.Description = "Right-click to select model";
    }
  }

  private void OnModelSelected(Model? model)
  {
    _menu?.Close();
    SelectedModel = model;
    RedrawMenuButton(model);
    ModelSelected?.Invoke(this, new ModelSelectedEventArgs(model));
  }
}
