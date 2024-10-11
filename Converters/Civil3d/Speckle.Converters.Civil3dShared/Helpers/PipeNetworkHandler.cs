using Speckle.Converters.Civil3dShared.Extensions;
using Speckle.Converters.Common;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Converters.Civil3dShared.Helpers;

public sealed class PipeNetworkHandler
{
  /// <summary>
  /// Keeps track of all networks used by parts in the current send operation.
  /// (network app id, network Proxy).
  /// This should be added to the root commit object post conversion.
  /// </summary>
  /// POC: Using group proxies for now
  public Dictionary<string, GroupProxy> PipeNetworkProxies { get; } = new();

  private readonly IConverterSettingsStore<Civil3dConversionSettings> _converterSettings;

  public PipeNetworkHandler(IConverterSettingsStore<Civil3dConversionSettings> converterSettings)
  {
    _converterSettings = converterSettings;
  }

  /// <summary>
  /// Extracts the pipe network from a part and stores in <see cref="PipeNetworkProxies"/> the appId of the part.
  /// </summary>
  /// <param name="part"></param>
  /// <returns></returns>
  public void HandlePipeNetwork(CDB.Part part)
  {
    if (part.NetworkId == ADB.ObjectId.Null)
    {
      return;
    }

    string networkApplicationId = part.NetworkId.GetSpeckleApplicationId();
    string partApplicationId = part.GetSpeckleApplicationId();
    if (PipeNetworkProxies.TryGetValue(networkApplicationId, out GroupProxy? value))
    {
      value.objects.Add(partApplicationId);
    }
    else
    {
      using (var tr = _converterSettings.Current.Document.Database.TransactionManager.StartTransaction())
      {
        var network = (CDB.Network)tr.GetObject(part.NetworkId, ADB.OpenMode.ForRead);

        PipeNetworkProxies[networkApplicationId] = new()
        {
          name = network.Name,
          objects = new() { partApplicationId },
          applicationId = networkApplicationId
        };

        tr.Commit();
      }
    }
    return;
  }
}
