using Speckle.Sdk.SQLite;

namespace Speckle.Importers.Rhino.Internal;

/// <summary>
/// Dummy implementation of <see cref="ISqLiteJsonCacheManager"/> to avoid
/// </summary>
public sealed class DummySqliteJsonCacheManager : ISqLiteJsonCacheManager
{
  public void Dispose() { }

  public IReadOnlyCollection<(string Id, string Json)> GetAllObjects() => [];

  public void DeleteObject(string id) { }

  public string? GetObject(string id) => null;

  public void SaveObject(string id, string json) { }

  public void UpdateObject(string id, string json) { }

  public void SaveObjects(IEnumerable<(string id, string json)> items) { }

  public bool HasObject(string objectId) => false;
}

public sealed class DummySqliteJsonCacheManagerFactory : ISqLiteJsonCacheManagerFactory
{
  private static readonly ISqLiteJsonCacheManager s_instance = new DummySqliteJsonCacheManager();

  public ISqLiteJsonCacheManager CreateForUser(string scope) => s_instance;

  public ISqLiteJsonCacheManager CreateFromStream(string streamId) => s_instance;
}
