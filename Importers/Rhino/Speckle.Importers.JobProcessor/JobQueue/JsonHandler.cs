using System.Data;
using System.Text.Json;
using Dapper;

namespace Speckle.Importers.JobProcessor.JobQueue;

internal sealed class JsonHandler<T> : SqlMapper.TypeHandler<T>
{
  private readonly JsonSerializerOptions _options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

  public override void SetValue(IDbDataParameter parameter, T? value)
  {
    parameter.Value = JsonSerializer.Serialize(value);
  }

  public override T? Parse(object value)
  {
    if (value is string json)
    {
      return JsonSerializer.Deserialize<T>(json, _options);
    }

    throw new DataException($"Cannot convert {value.GetType()} to {typeof(T)}");
  }
}
