using Speckle.Newtonsoft.Json;

namespace Speckle.Connectors.DUI.Utils;

public interface IJsonSerializer
{
  string Serialize(object? obj);
  T? Deserialize<T>(string json)
    where T : class;
  object? Deserialize(string json, Type type);
}

public class JsonSerializer(IJsonSerializerSettingsFactory jsonSerializerSettingsFactory) : IJsonSerializer
{
  private readonly JsonSerializerSettings _serializerOptions = jsonSerializerSettingsFactory.Create();

  public string Serialize(object? obj) => JsonConvert.SerializeObject(obj, _serializerOptions);

  // POC: this seemms more like a IModelsDeserializer?, seems disconnected from this class
  public T? Deserialize<T>(string json)
    where T : class => JsonConvert.DeserializeObject<T>(json, _serializerOptions);

  public object? Deserialize(string json, Type type) => JsonConvert.DeserializeObject(json, type, _serializerOptions);
}
