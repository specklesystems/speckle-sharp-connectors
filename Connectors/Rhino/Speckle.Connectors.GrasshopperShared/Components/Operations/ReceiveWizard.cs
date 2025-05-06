using Speckle.Connectors.GrasshopperShared.Components.Operations.Send;
using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Models;
using Speckle.Sdk.Credentials;
using Version = Speckle.Sdk.Api.GraphQL.Models.Version;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations;

public class ReceiveWizard : SendWizard
{
  public Version? SelectedVersion { get; }
  public VersionMenuHandler VersionMenuHandler { get; }
  public GhContextMenuButton VersionContextMenuButton { get; }

  public ResourceCollection<Version>? LastFetchedVersions { get; private set; }

  public ReceiveWizard(Account account, Func<Task> refreshComponent)
    : base(account, refreshComponent)
  {
    VersionMenuHandler = new VersionMenuHandler(FetchMoreVersions);
    VersionContextMenuButton = VersionMenuHandler.VersionContextMenuButton;
  }

  /// <summary>
  /// Callback function to retrieve amount of versions
  /// </summary>
  private async Task<ResourceCollection<Version>> FetchMoreVersions(int versionCount)
  {
    if (SelectedAccount == null || SelectedProject == null || SelectedModel == null)
    {
      return new ResourceCollection<Version>();
    }

    IClient client = ClientFactory.Create(SelectedAccount);
    var newVersionsResult = await client
      .Model.GetWithVersions(SelectedModel.id, SelectedProject.id, versionCount)
      .ConfigureAwait(true);
    LastFetchedVersions = newVersionsResult.versions;
    return newVersionsResult.versions;
  }
}
