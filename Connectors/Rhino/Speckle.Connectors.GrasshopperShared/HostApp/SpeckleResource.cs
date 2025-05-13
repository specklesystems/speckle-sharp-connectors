using Speckle.Connectors.Common.Operations;
using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Models;
using Speckle.Sdk.Common;
using Version = Speckle.Sdk.Api.GraphQL.Models.Version;

namespace Speckle.Connectors.GrasshopperShared.HostApp;

public abstract record SpeckleUrlModelResource(string Server, string ProjectId)
{
  public abstract Task<ReceiveInfo> GetReceiveInfo(IClient client, CancellationToken cancellationToken = default);

  public abstract Task<SendInfo> GetSendInfo(IClient client, CancellationToken cancellationToken = default);
}

public record SpeckleUrlLatestModelVersionResource(string Server, string ProjectId, string ModelId)
  : SpeckleUrlModelResource(Server, ProjectId)
{
  public override async Task<ReceiveInfo> GetReceiveInfo(IClient client, CancellationToken cancellationToken = default)
  {
    Project project = await client.Project.Get(ProjectId, cancellationToken).ConfigureAwait(false);
    ModelWithVersions model = await client
      .Model.GetWithVersions(ModelId, ProjectId, 1, null, null, cancellationToken)
      .ConfigureAwait(false);
    Version version = model.versions.items[0];

    var info = new ReceiveInfo(
      client.Account.id,
      new Uri(Server),
      ProjectId,
      project.name,
      ModelId,
      model.name,
      version.id,
      version.sourceApplication.NotNull()
    );

    return info;
  }

  public override async Task<SendInfo> GetSendInfo(IClient client, CancellationToken cancellationToken = default)
  {
    // We don't care about the return info, we just want to be sure we have access and everything exists.
    await client.Project.Get(ProjectId, cancellationToken).ConfigureAwait(false);
    await client.Model.Get(ModelId, ProjectId, cancellationToken).ConfigureAwait(false);

    return new SendInfo(
      client.Account.id,
      new Uri(Server),
      ProjectId,
      ModelId,
      "Grasshopper8" // TODO: Grab from the right place!
    );
  }
}

public record SpeckleUrlModelVersionResource(string Server, string ProjectId, string ModelId, string VersionId)
  : SpeckleUrlModelResource(Server, ProjectId)
{
  public override async Task<ReceiveInfo> GetReceiveInfo(IClient client, CancellationToken cancellationToken = default)
  {
    Project project = await client.Project.Get(ProjectId, cancellationToken).ConfigureAwait(false);
    Model model = await client.Model.Get(ModelId, ProjectId, cancellationToken).ConfigureAwait(false);
    Version version = await client.Version.Get(VersionId, ProjectId, cancellationToken).ConfigureAwait(false);

    var info = new ReceiveInfo(
      client.Account.id,
      new Uri(Server),
      ProjectId,
      project.name,
      ModelId,
      model.name,
      VersionId,
      version.sourceApplication.NotNull()
    );

    return info;
  }

  public override async Task<SendInfo> GetSendInfo(IClient client, CancellationToken cancellationToken = default)
  {
    // We don't care about the return info, we just want to be sure we have access and everything exists.
    await client.Project.Get(ProjectId, cancellationToken).ConfigureAwait(false);
    await client.Model.Get(ModelId, ProjectId, cancellationToken).ConfigureAwait(false);

    return new SendInfo(
      client.Account.id,
      new Uri(Server),
      ProjectId,
      ModelId,
      "Grasshopper8" // TODO: Grab from the right place!
    );
  }
}

public record SpeckleUrlModelObjectResource(string Server, string ProjectId, string ObjectId)
  : SpeckleUrlModelResource(Server, ProjectId)
{
  public override Task<ReceiveInfo> GetReceiveInfo(IClient client, CancellationToken cancellationToken = default) =>
    throw new NotImplementedException("Object Resources are not supported yet");

  public override Task<SendInfo> GetSendInfo(IClient client, CancellationToken cancellationToken = default) =>
    throw new NotImplementedException("Object Resources are not supported yet");
}
