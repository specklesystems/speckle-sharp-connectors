using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Onova;
using Onova.Exceptions;
using Onova.Models;
using Speckle.InterfaceGenerator;

namespace Speckle.Connectors.Logging.Updates;

[GenerateAutoInterface]
#pragma warning disable CA1001
public sealed class ConnectorUpdateService
#pragma warning restore CA1001
{
  private readonly IUpdateManager? _updateManager;
  private readonly ILogger<ConnectorUpdateService> _logger;
  internal ConnectorUpdateService(string name, string slug, ILogger<ConnectorUpdateService> logger,
    ILogger<ConnectorFeedResolver> feedLogger)
  {
    _updateManager = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ?
      new UpdateManager(
        new AssemblyMetadata(name,s_assembly.GetName().Version ?? throw new InvalidOperationException("Cannot get version"), s_assembly.Location),
        new ConnectorFeedResolver(slug, feedLogger
        ),
        new InnoSetupExecutor()
      ) : null;
    _logger = logger;
  }
  private static readonly Assembly s_assembly = Assembly.GetExecutingAssembly();

    private Version? _updateVersion;
    private bool _updatePrepared;
    private bool _updaterLaunched;

    public async Task<Version?> CheckForUpdatesAsync()
    {
      if (_updateManager is null)
      {
        return null;
      }
      _logger.LogInformation("Checking for updates...");
        var check = await _updateManager.CheckForUpdatesAsync();
        
        _logger.LogInformation($"Found updates: {check.CanUpdate}");
        return check.CanUpdate ? check.LastVersion : null;
    }

    public async Task PrepareUpdateAsync(Version version)
    {
      if (_updateManager is null)
      {
        return;
      }
        try
        {
          _logger.LogInformation("Preparing for updates...");
            await _updateManager.PrepareUpdateAsync(_updateVersion = version);
            _updatePrepared = true;
        }
        catch (UpdaterAlreadyLaunchedException e)
        {
            // Ignore race conditions
            _logger.LogInformation(e, "Error in preparing for updates...");
        }
        catch (LockFileNotAcquiredException e)
        {
            // Ignore race conditions
            _logger.LogInformation(e, "Error in preparing for updates...");
        }
    }

    public void FinalizeUpdate(bool needRestart)
    {
        if (_updateManager is null || _updateVersion is null || !_updatePrepared || _updaterLaunched)
        {
          return;
        }

        try
        {
          _logger.LogInformation($"Launching updater... {needRestart}");
            _updateManager.LaunchUpdater(_updateVersion, needRestart);
            _updaterLaunched = true;
        }
        catch (UpdaterAlreadyLaunchedException e)
        {
            // Ignore race conditions
            _logger.LogInformation(e, "Error in preparing for updates...");
        }
        catch (LockFileNotAcquiredException e)
        {
            // Ignore race conditions
            _logger.LogInformation(e, "Error in preparing for updates...");
        }
    }

    public void Dispose() => _updateManager?.Dispose();
}
