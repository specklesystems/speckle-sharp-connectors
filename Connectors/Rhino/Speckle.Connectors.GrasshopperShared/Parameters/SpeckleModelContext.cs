namespace Speckle.Connectors.GrasshopperShared.Parameters;

/// <summary>
/// Where a wrapper came from. Lets a consumer find the cached bundle at <c>%TEMP%\Speckle\receive\{VersionId}</c>.
/// </summary>
/// <remarks>
/// Shared by reference across one receive. Null on canvas-authored wrappers. No account by design - if the cache is
/// gone, the consumer asks for a reload.
/// </remarks>
public sealed record SpeckleModelContext(string ProjectId, string VersionId);
