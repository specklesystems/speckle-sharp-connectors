using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Newtonsoft.Json;
using Speckle.Newtonsoft.Json.Linq;
using Speckle.Newtonsoft.Json.Serialization;
using Speckle.Sdk.Serialisation;

namespace Speckle.Connectors.DUI.Utils;

/// <summary>
/// This converter ensures we can do polymorphic deserialization to concrete types. This converter is intended
/// for use only with UI bound types, not Speckle Bases.
/// </summary>
//this is effectively a singleton because BrowserBridge and TopLevelExceptionHandler use it
public class DiscriminatedObjectConverter(IServiceProvider serviceProvider) : JsonConverter<DiscriminatedObject>
{
  // POC: remove, replace with DI
  private static readonly ConcurrentDictionary<string, Type?> s_typeCache = new();
  private static readonly Speckle.Newtonsoft.Json.JsonSerializer s_localSerializer =
    new()
    {
      DefaultValueHandling = DefaultValueHandling.Ignore,
      ContractResolver = new CamelCasePropertyNamesContractResolver(),
      NullValueHandling = NullValueHandling.Ignore,
    };

  public override void WriteJson(
    JsonWriter writer,
    DiscriminatedObject? value,
    Speckle.Newtonsoft.Json.JsonSerializer serializer
  )
  {
    if (value is null)
    {
      return;
    }
    var jo = JObject.FromObject(value, s_localSerializer);
    jo.WriteTo(writer);
  }

  public override DiscriminatedObject? ReadJson(
    JsonReader reader,
    Type objectType,
    DiscriminatedObject? existingValue,
    bool hasExistingValue,
    Speckle.Newtonsoft.Json.JsonSerializer serializer
  )
  {
    JObject jsonObject = JObject.Load(reader);

    var typeName =
      jsonObject.Value<string>("typeDiscriminator")
      ?? throw new SpeckleDeserializeException(
        "DUI3 Discriminator converter deserialization failed: did not find a typeDiscriminator field."
      );
    var type =
      GetTypeByName(typeName)
      ?? throw new SpeckleDeserializeException(
        "DUI3 Discriminator converter deserialization failed, type not found: " + typeName
      );
    var obj = ActivatorUtilities.CreateInstance(serviceProvider, type);
    serializer.Populate(jsonObject.CreateReader(), obj);

    // Store the JSON property names in the object for later comparison
    if (obj is PropertyValidator pv)
    {
      // Capture property names from JSON
      var jsonPropertyNames = jsonObject.Properties().Select(p => p.Name).ToList();

      pv.JsonPropertyNames = jsonPropertyNames;
    }

    // POC: cast? throw if null?
    return obj as DiscriminatedObject;
  }

  private static Type? GetTypeByName(string typeName) =>
    s_typeCache.GetOrAdd(
      typeName,
      name =>
      {
        //POC: why does this exist like this?
        // The assemblies within the CurrentDomain are not necessarily loaded
        // probably we can leverage DI here so we already know the types, possibly DI plus an attribute
        // then we can cache everything on startup
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().Reverse())
        {
          try
          {
            // POC: contains is weak
            // working by accident, ModelCard is contained within SenderModelCard :O
            // comparisons :D
            var type = assembly.DefinedTypes.FirstOrDefault(t => !string.IsNullOrEmpty(t?.Name) && t?.Name == name);
            if (type != null)
            {
              return type;
            }
          }
          // POC: this Exception pattern is too broad and should be resticted but fixes the above issues
          // the call above is causing load of all assemblies (which is also possibly not good)
          // AND it explodes for me loading an exception, so at the last this should
          // catch System.Reflection.ReflectionTypeLoadException (and anthing else DefinedTypes might throw)
          // LATER COMMENT: Since discriminated object is only used in DUI3 models, we could restrict to only "this" assembly?
          catch (ReflectionTypeLoadException ex)
          {
            // POC: logging
            Debug.WriteLine("***" + ex.Message);
          }
        }

        // should this throw instead? :/
        return null;
      }
    );
}

public class AbstractConverter<TReal, TAbstract> : JsonConverter
{
  public override bool CanConvert(Type objectType) => objectType == typeof(TAbstract);

  public override object? ReadJson(
    JsonReader reader,
    Type objectType,
    object? existingValue,
    Speckle.Newtonsoft.Json.JsonSerializer serializer
  ) => serializer.Deserialize<TReal>(reader);

  public override void WriteJson(JsonWriter writer, object? value, Speckle.Newtonsoft.Json.JsonSerializer serializer) =>
    serializer.Serialize(writer, value);
}
