using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.GrasshopperShared.Components.Operations.Receive;
using Speckle.Connectors.GrasshopperShared.Components.Operations.Send;
using Speckle.Connectors.GrasshopperShared.Registration;
using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Models;
using Speckle.Sdk.Common;
using Speckle.Sdk.Credentials;
using Version = Speckle.Sdk.Api.GraphQL.Models.Version;

namespace Speckle.Connectors.GrasshopperShared.HostApp;

// noting that if the user inputs a model url string, this will not contain account info
// (and that's why the accountID is nullable in the record resource)
public abstract record SpeckleUrlModelResource(AccountResource Account, string? WorkspaceId, string ProjectId)
{
  public abstract Task<GrasshopperReceiveInfo> GetReceiveInfo(
    IClient client,
    CancellationToken cancellationToken = default
  );

  public abstract Task<GrasshopperSendInfo> GetSendInfo(IClient client, CancellationToken cancellationToken = default);
}

public record SpeckleUrlLatestModelVersionResource(
  AccountResource Account,
  string? WorkspaceId,
  string ProjectId,
  string ModelId
) : SpeckleUrlModelResource(Account, WorkspaceId, ProjectId)
{
  public override async Task<GrasshopperReceiveInfo> GetReceiveInfo(
    IClient client,
    CancellationToken cancellationToken = default
  )
  {
    Project project = await client.Project.Get(ProjectId, cancellationToken).ConfigureAwait(false);
    ModelWithVersions model = await client
      .Model.GetWithVersions(ModelId, ProjectId, 1, null, null, cancellationToken)
      .ConfigureAwait(false);
    Version version = model.versions.items[0];

    var info = new GrasshopperReceiveInfo(
      client.Account,
      project.workspaceId,
      ProjectId,
      project.name,
      ModelId,
      model.name,
      version.id,
      version.sourceApplication.NotNull(),
      HostApplications.Grasshopper.Slug,
      version.authorUser?.id
    );

    return info;
  }

  public override async Task<GrasshopperSendInfo> GetSendInfo(
    IClient client,
    CancellationToken cancellationToken = default
  )
  {
    // We don't care about the return info, we just want to be sure we have access and everything exists.
    await client.Project.Get(ProjectId, cancellationToken).ConfigureAwait(false);
    await client.Model.Get(ModelId, ProjectId, cancellationToken).ConfigureAwait(false);

    return new GrasshopperSendInfo(client, WorkspaceId, ProjectId, ModelId);
  }
}

public record SpeckleUrlModelVersionResource(
  AccountResource Account,
  string? WorkspaceId,
  string ProjectId,
  string ModelId,
  string VersionId
) : SpeckleUrlModelResource(Account, WorkspaceId, ProjectId)
{
  public override async Task<GrasshopperReceiveInfo> GetReceiveInfo(
    IClient client,
    CancellationToken cancellationToken = default
  )
  {
    Project project = await client.Project.Get(ProjectId, cancellationToken).ConfigureAwait(false);
    Model model = await client.Model.Get(ModelId, ProjectId, cancellationToken).ConfigureAwait(false);
    Version version = await client.Version.Get(VersionId, ProjectId, cancellationToken).ConfigureAwait(false);

    var info = new GrasshopperReceiveInfo(
      client.Account,
      project.workspaceId,
      ProjectId,
      project.name,
      ModelId,
      model.name,
      VersionId,
      version.sourceApplication.NotNull(),
      HostApplications.Grasshopper.Slug,
      version.authorUser?.id
    );

    return info;
  }

  public override async Task<GrasshopperSendInfo> GetSendInfo(
    IClient client,
    CancellationToken cancellationToken = default
  )
  {
    // We don't care about the return info, we just want to be sure we have access and everything exists.
    await client.Project.Get(ProjectId, cancellationToken).ConfigureAwait(false);
    await client.Model.Get(ModelId, ProjectId, cancellationToken).ConfigureAwait(false);

    return new GrasshopperSendInfo(client, WorkspaceId, ProjectId, ModelId);
  }
}

public record SpeckleUrlModelObjectResource(
  AccountResource Account,
  string? WorkspaceId,
  string ProjectId,
  string ObjectId
) : SpeckleUrlModelResource(Account, WorkspaceId, ProjectId)
{
  public override Task<GrasshopperReceiveInfo> GetReceiveInfo(
    IClient client,
    CancellationToken cancellationToken = default
  ) => throw new NotImplementedException("Object Resources are not supported yet");

  public override Task<GrasshopperSendInfo> GetSendInfo(
    IClient client,
    CancellationToken cancellationToken = default
  ) => throw new NotImplementedException("Object Resources are not supported yet");
}

public record AccountResource(string? AccountId, string? Token, string Server)
{
  public Account? GetAccount(IServiceScope scope)
  {
    if (Token is not null)
    {
      return scope.Get<IAccountFactory>().CreateAccount(new Uri(Server), Token).GetAwaiter().GetResult();
    }
    return AccountId != null
      ? scope.Get<IAccountManager>().GetAccount(AccountId)
      : scope.Get<IAccountService>().GetAccountWithServerUrlFallback("", new Uri(Server)); // fallback the account that matches with URL if a
  }
}
