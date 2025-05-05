// using Speckle.Sdk.Api.GraphQL.Models;
//
// namespace Speckle.Connectors.GrasshopperShared.Components.Operations;
//
// public class ModelSelectedEventArgs(Model? project) : EventArgs
// {
//   public Model? SelectedProject { get; } = project;
// }
//
// /// <summary>
// /// Helper class to manage model filtering and selection for the components.
// /// </summary>
// public class ModelMenuHandler
// {
//   private readonly Func<string, Task<ResourceCollection<Model>>> _fetchModels;
//   private ToolStripDropDown? _menu;
//   private SearchToolStripMenuItem? _searchItem;
//   private Model? SelectedModel { get; set; }
//
//   public ResourceCollection<Model>? Models { get; set; }
//
//   public event EventHandler<ModelSelectedEventArgs>? ModelSelected;
//
//   public GhContextMenuButton ModelContextMenuButton { get; set; }
//
//   public ModelMenuHandler(Func<string, Task<ResourceCollection<Model>>> fetchModels)
//   {
//     _fetchModels = fetchModels;
//     ModelContextMenuButton = new GhContextMenuButton(
//       "Select Model",
//       "Select Project",
//       "Right-click to select a model",
//       PopulateMenu
//     );
//   }
//
//   private async Task Refetch(string searchText)
//   {
//     Models = await _fetchModels.Invoke(searchText);
//     PopulateMenu(_menu!);
//   }
//
//   private bool PopulateMenu(ToolStripDropDown menu)
//   {
//     _menu = menu;
//     _menu.Closed += (sender, args) =>
//     {
//       _searchItem = null;
//     };
//     if (Models == null)
//     {
//       Menu_AppendItem(menu, "No models were fetched");
//       return true;
//     }
//
//     if (Models.items.Count == 0)
//     {
//       Menu_AppendItem(menu, "Project has no models");
//       return true;
//     }
//
//     PopulateModelMenuItems(menu);
//
//     return true;
//   }
//
//   private void PopulateModelMenuItems(ToolStripDropDown menu)
//   {
//     var lastIndex = menu.Items.Count - 1;
//     if (lastIndex >= 0)
//     {
//       // clean the existing items because we re-populate when user search
//       for (int i = lastIndex; i > 1; i--)
//       {
//         menu.Items.RemoveAt(i);
//       }
//     }
//
//     if (LastFetchedModels == null)
//     {
//       return;
//     }
//
//     if (SearchModelToolStripMenuItem == null)
//     {
//       SearchModelToolStripMenuItem = new SearchToolStripMenuItem(menu, RefetchModels);
//     }
//
//     Menu_AppendSeparator(menu);
//
//     foreach (var model in LastFetchedModels.items)
//     {
//       var desc = string.IsNullOrEmpty(model.description) ? "No description" : model.description;
//
//       Menu_AppendItem(
//         menu,
//         $"{model.name} - {desc}",
//         (_, _) => OnModelSelected(model),
//         null,
//         _model?.id != model.id,
//         _model?.id == model.id
//       );
//     }
//   }
// }
