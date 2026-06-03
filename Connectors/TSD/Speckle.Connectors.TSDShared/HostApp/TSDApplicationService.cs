using Microsoft.Extensions.Logging;
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
          result.Add(new TSDSelectedEntity(entity.Id.ToString(), entity.Type.ToString()));
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

      var members = await model.GetMembersAsync(null).ConfigureAwait(false);
      if (members is null)
      {
        return Array.Empty<IMember>();
      }

      if (objectIds.Count == 0)
      {
        return members.ToList();
      }

      var idSet = objectIds.ToHashSet();
      return members.Where(member => idSet.Contains(member.Id.ToString())).ToList();
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to read TSD members for send");
      return Array.Empty<IMember>();
    }
  }

  public async Task<IUnitBase?> GetLengthUnitAsync()
  {
    if (Application is null)
    {
      return null;
    }

    try
    {
      var document = await Application.GetDocumentAsync().ConfigureAwait(false);
      if (document is null)
      {
        return null;
      }

      var model = await document.GetModelAsync().ConfigureAwait(false);
      if (model is null)
      {
        return null;
      }

      var settings = await model.GetSettingsAsync(default).ConfigureAwait(false);
      var unitSettings = settings?.UnitSettings.Value;
      if (unitSettings is null)
      {
        return null;
      }

      var units = await unitSettings.GetUnitsV2Async(new[] { Quantity.Distance }, default).ConfigureAwait(false);
      return units?.FirstOrDefault();
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to read TSD model units");
      return null;
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
      asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
    else
    {
      Application?.Dispose();
    }
  }
}
