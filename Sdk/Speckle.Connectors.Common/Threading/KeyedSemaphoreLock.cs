using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Speckle.InterfaceGenerator;

namespace Speckle.Connectors.Common.Threading;

public partial interface IKeyedSemaphoreLock : IDisposable;
[GenerateAutoInterface]
public sealed class KeyedSemaphoreLock(ILogger<KeyedSemaphoreLock> logger) : IKeyedSemaphoreLock
{
    private  readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new();

    public void Dispose()
    {
      foreach (var item in _semaphores)
      {
        item.Value.Dispose();
      }
    }

    public async Task<IDisposable> AcquireLockAsync(string key, CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key cannot be null or whitespace.", nameof(key));
        }

        SemaphoreSlim semaphore = _semaphores.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

        bool acquiredImmediately = await semaphore.WaitAsync(TimeSpan.Zero, cancellation);

        if (!acquiredImmediately)
        {
          logger.LogError($"Thread is WAITING for lock on key: '{key}'.");
          await semaphore.WaitAsync(cancellation);
        }

        return new Releaser(semaphore);
    }

    private class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                semaphore.Release();
                _disposed = true;
            }
        }
    }
}
