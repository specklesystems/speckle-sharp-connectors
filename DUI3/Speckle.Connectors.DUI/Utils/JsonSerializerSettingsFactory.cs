using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.InterfaceGenerator;
using Speckle.Newtonsoft.Json;
using Speckle.Newtonsoft.Json.Serialization;

namespace Speckle.Connectors.DUI.Utils;

[GenerateAutoInterface]
public class JsonSerializerSettingsFactory(IServiceProvider serviceProvider) : IJsonSerializerSettingsFactory
{
  public JsonSerializerSettings Create()
  {
    // Register WebView2 panel stuff
    JsonSerializerSettings settings =
      new()
      {
        Error = (_, args) =>
        {
          // POC: we should probably do a bit more than just swallowing this!
          Console.WriteLine("*** JSON ERROR: " + args.ErrorContext);
        },
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate,
        Converters =
        {
          new DiscriminatedObjectConverter(serviceProvider),
          new AbstractConverter<DiscriminatedObject, ISendFilter>()
        }
      };
    return settings;
  }
}
