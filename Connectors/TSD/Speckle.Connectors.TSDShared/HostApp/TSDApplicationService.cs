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

  public async Task<IReadOnlyList<IEntity>> GetObjectsForSendAsync(IReadOnlyList<string> objectIds)
  {
    if (Application is null)
    {
      return Array.Empty<IEntity>();
    }

    try
    {
      var document = await Application.GetDocumentAsync().ConfigureAwait(false);
      if (document is null)
      {
        return Array.Empty<IEntity>();
      }

      var model = await document.GetModelAsync().ConfigureAwait(false);
      if (model is null)
      {
        return Array.Empty<IEntity>();
      }

      var result = new List<IEntity>();

      if (objectIds.Count == 0)
      {
        result.AddRange(await model.GetMembersAsync(null).ConfigureAwait(false) ?? Enumerable.Empty<IMember>());
        result.AddRange(await model.GetSlabItemsAsync(null).ConfigureAwait(false) ?? Enumerable.Empty<ISlabItem>());
        result.AddRange(
          await model.GetStructuralWallsAsync(null).ConfigureAwait(false) ?? Enumerable.Empty<IStructuralWall>()
        );
        return result;
      }

      var memberIndices = new List<int>();
      var slabIndices = new List<int>();
      var slabItemIndices = new List<int>();
      var wallIndices = new List<int>();
      foreach (var objectId in objectIds)
      {
        var (type, index) = TsdObjectIdentifier.Decode(objectId);
        switch (type)
        {
          case EntityType.Member:
            memberIndices.Add(index);
            break;
          case EntityType.Slab:
            slabIndices.Add(index);
            break;
          case EntityType.SlabItem:
            slabItemIndices.Add(index);
            break;
          case EntityType.StructuralWall:
            wallIndices.Add(index);
            break;
        }
      }

      if (memberIndices.Count > 0)
      {
        result.AddRange(
          await model.GetMembersAsync(memberIndices).ConfigureAwait(false) ?? Enumerable.Empty<IMember>()
        );
      }

      if (wallIndices.Count > 0)
      {
        result.AddRange(
          await model.GetStructuralWallsAsync(wallIndices).ConfigureAwait(false) ?? Enumerable.Empty<IStructuralWall>()
        );
      }

      if (slabIndices.Count > 0)
      {
        var slabs = await model.GetSlabsAsync(slabIndices).ConfigureAwait(false);
        if (slabs is not null)
        {
          slabItemIndices.AddRange(slabs.SelectMany(slab => slab.SlabItemIndices));
        }
      }

      if (slabItemIndices.Count > 0)
      {
        result.AddRange(
          await model.GetSlabItemsAsync(slabItemIndices.Distinct()).ConfigureAwait(false)
            ?? Enumerable.Empty<ISlabItem>()
        );
      }

      return result;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to read TSD objects for send");
      return Array.Empty<IEntity>();
    }
  }

  public async Task<IReadOnlyDictionary<int, ISlabData>> GetSlabDataAsync(IEnumerable<int> slabIndices)
  {
    var result = new Dictionary<int, ISlabData>();
    if (Application is null)
    {
      return result;
    }

    var indices = slabIndices.Distinct().ToList();
    if (indices.Count == 0)
    {
      return result;
    }

    try
    {
      var document = await Application.GetDocumentAsync().ConfigureAwait(false);
      var model = document is null ? null : await document.GetModelAsync().ConfigureAwait(false);
      if (model is null)
      {
        return result;
      }

      var slabs = await model.GetSlabsAsync(indices).ConfigureAwait(false);
      if (slabs is null)
      {
        return result;
      }

      foreach (var slab in slabs)
      {
        var data = slab.SlabData.Value;
        if (data is not null)
        {
          result[slab.Index] = data;
        }
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to read TSD slab data");
    }

    return result;
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
