using Speckle.Sdk.SQLite;

namespace Speckle.Importers.Ifc.Tester;

public sealed class DummySendCacheManager(Dictionary<string, string> objects) : ISqLiteJsonCacheManager
{
  public void Dispose() { }

  public IReadOnlyCollection<(string, string)> GetAllObjects() => throw new NotImplementedException();

  public void DeleteObject(string id) => throw new NotImplementedException();

  public string? GetObject(string id) => null;

  public void SaveObject(string id, string json) => throw new NotImplementedException();

  public bool HasObject(string objectId) => false;
#pragma warning disable CA1065
  public string Path => throw new NotImplementedException();
#pragma warning restore CA1065

  public void SaveObjects(IEnumerable<(string id, string json)> items)
  {
    foreach (var (id, json) in items)
    {
      objects[id] = json;
    }
  }

  public void UpdateObject(string id, string json) => throw new NotImplementedException();
}
