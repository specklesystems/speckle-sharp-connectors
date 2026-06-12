using Microsoft.Extensions.Logging;
using Speckle.Connectors.TSDShared.Utils;
using Speckle.Sdk;
using TSD.API.Remoting;
using TSD.API.Remoting.Common;
using TSD.API.Remoting.Common.Interfaces;
using TSD.API.Remoting.Structure;
using TSD.API.Remoting.Units;

namespace Speckle.Connectors.TSDShared.HostApp;

internal sealed class TSDApplicationService : ITSDApplicationService, IDisposable
{
  private readonly ILogger<TSDApplicationService> _logger;
  private bool _disposed;

  public TSDApplicationService(ILogger<TSDApplicationService> logger)
  {
    _logger = logger;
  }

  public int? Port { get; private set; }
  public IApplication? Application { get; private set; }
  public string? ApplicationTitle { get; private set; }
  public string? ApplicationVersion { get; private set; }
  public Guid? ModelId { get; private set; }
  public bool IsConnected => Application is not null;

  public event EventHandler? SelectionChanged;

  public async Task ConnectAsync(int port)
  {
    Port = port;

    try
    {
      Application = await ApplicationFactory.ConnectToRunningApplicationAsync(port).ConfigureAwait(false);

      if (Application is null)
      {
        _logger.LogWarning("Could not connect to TSD on port {Port}", port);
        return;
      }

      Application.SelectionChanged += OnApplicationSelectionChanged;

      ApplicationTitle = await Application.GetApplicationTitleAsync().ConfigureAwait(false);
      ApplicationVersion = await Application.GetVersionStringAsync().ConfigureAwait(false);

      var document = await Application.GetDocumentAsync().ConfigureAwait(false);
      ModelId = document?.ModelId;

      _logger.LogInformation(
        "Connected to TSD: {Title} ({Version}) on port {Port}",
        ApplicationTitle,
        ApplicationVersion,
        port
      );
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to connect to TSD on port {Port}", port);
    }
  }

  public async Task<IReadOnlyList<TSDSelectedEntity>> GetSelectedEntitiesAsync()
  {
    if (Application is null)
    {
      return Array.Empty<TSDSelectedEntity>();
    }

    try
    {
      var document = await Application.GetDocumentAsync().ConfigureAwait(false);
      if (document is null)
      {
        return Array.Empty<TSDSelectedEntity>();
      }

      var model = await document.GetModelAsync().ConfigureAwait(false);
      if (model is null)
      {
        return Array.Empty<TSDSelectedEntity>();
      }

      var selection = await model.GetSelectedEntitiesAsync().ConfigureAwait(false);
      if (selection is null)
      {
        return Array.Empty<TSDSelectedEntity>();
      }

      var result = new List<TSDSelectedEntity>();
      foreach (var item in selection)
      {
        if (item is ISelectedEntity selectedEntity)
        {
          var entity = selectedEntity.Entity;
          result.Add(
            new TSDSelectedEntity(TsdObjectIdentifier.Encode(entity.Type, entity.Index), entity.Type.ToString())
          );
        }
      }

      return result;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to read the active TSD selection");
      return Array.Empty<TSDSelectedEntity>();
    }
  }

  public async Task<IReadOnlyList<IMember>> GetMembersForSendAsync(IReadOnlyList<string> objectIds)
  {
    if (Application is null)
    {
      return Array.Empty<IMember>();
    }

    try
    {
      var document = await Application.GetDocumentAsync().ConfigureAwait(false);
      if (document is null)
      {
        return Array.Empty<IMember>();
      }

      var model = await document.GetModelAsync().ConfigureAwait(false);
      if (model is null)
      {
        return Array.Empty<IMember>();
      }

      if (objectIds.Count == 0)
      {
        var allMembers = await model.GetMembersAsync(null).ConfigureAwait(false);
        return allMembers?.ToList() ?? (IReadOnlyList<IMember>)Array.Empty<IMember>();
      }

      var memberIndices = new List<int>();
      foreach (var objectId in objectIds)
      {
        var (type, index) = TsdObjectIdentifier.Decode(objectId);
        if (type == EntityType.Member)
        {
          memberIndices.Add(index);
        }
      }

      if (memberIndices.Count == 0)
      {
        return Array.Empty<IMember>();
      }

      var members = await model.GetMembersAsync(memberIndices).ConfigureAwait(false);
      return members?.ToList() ?? (IReadOnlyList<IMember>)Array.Empty<IMember>();
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to read TSD members for send");
      return Array.Empty<IMember>();
    }
  }

  public async Task<IReadOnlyDictionary<Quantity, IUnitBase>> GetUnitsAsync(IEnumerable<Quantity> quantities)
  {
    var result = new Dictionary<Quantity, IUnitBase>();
    if (Application is null)
    {
      return result;
    }

    try
    {
      var document = await Application.GetDocumentAsync().ConfigureAwait(false);
      if (document is null)
      {
        return result;
      }

      var model = await document.GetModelAsync().ConfigureAwait(false);
      if (model is null)
      {
        return result;
      }

      var settings = await model.GetSettingsAsync(default).ConfigureAwait(false);
      var unitSettings = settings?.UnitSettings.Value;
      if (unitSettings is null)
      {
        return result;
      }

      foreach (var quantity in quantities.Distinct())
      {
        var units = await unitSettings.GetUnitsV2Async(new[] { quantity }, default).ConfigureAwait(false);
        if (units?.FirstOrDefault() is IUnitBase unit)
        {
          result[quantity] = unit;
        }
      }

      return result;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to read TSD model units");
      return result;
    }
  }

  public async Task<IReadOnlyList<double>> ConvertFromBaseAsync(IReadOnlyList<double> values, IUnitBase unit)
  {
    if (Application is null || values.Count == 0)
    {
      return values;
    }

    try
    {
      var converted = await Application.UnitConverter.FromBaseAsync(values, unit, default).ConfigureAwait(false);
      return converted?.ToList() ?? values;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to convert TSD coordinates from base units");
      return values;
    }
  }

  private void OnApplicationSelectionChanged(object? sender, EventArgs e) =>
    SelectionChanged?.Invoke(this, EventArgs.Empty);

  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }
    _disposed = true;

    if (Application is not null)
    {
      Application.SelectionChanged -= OnApplicationSelectionChanged;
    }

    if (Application is IAsyncDisposable asyncDisposable)
    {
      Task.Run(() => asyncDisposable.DisposeAsync().AsTask()).GetAwaiter().GetResult();
    }
    else
    {
      Application?.Dispose();
    }
  }
}
