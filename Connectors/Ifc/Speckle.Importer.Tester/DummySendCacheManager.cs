using Speckle.Sdk.SQLite;

namespace Speckle.Importer.Tester;

public class DummySendCacheManager(Dictionary<string, string> objects) : ISqLiteJsonCacheManager
{
  public void Dispose() { }

  public IReadOnlyCollection<(string, string)> GetAllObjects() => throw new NotImplementedException();

  public void DeleteObject(string id) => throw new NotImplementedException();

  public string? GetObject(string id) => null;

  public void SaveObject(string id, string json) => throw new NotImplementedException();

  public bool HasObject(string objectId) => false;

  public void SaveObjects(IEnumerable<(string id, string json)> items)
  {
    foreach (var (id, json) in items)
    {
      objects[id] = json;
    }
  }

  public void UpdateObject(string id, string json) => throw new NotImplementedException();
}
