using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Extensions;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Testing;
using Speckle.HostApps.Framework;
using Speckle.Sdk;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Models;
using Speckle.Sdk.Serialisation;
using Speckle.Sdk.Serialisation.V2;
using Speckle.Sdk.Serialisation.V2.Receive;
using Speckle.Sdk.Serialisation.V2.Send;
using Speckle.Sdk.SQLite;
using Speckle.Sdk.Transports;

namespace Speckle.HostApps;

public static class ServiceCollectionExtensions
{
  public static void AddHostAppTesting<TTestBinding>(this IServiceCollection services)
    where TTestBinding : class, IBinding
  {
    services.AddSingleton<IBinding, TTestBinding>();
    services.AddMatchingInterfacesAsTransient(typeof(TestExecutor).Assembly);
  }

  public static void UseHostAppTesting(this IServiceCollection serviceCollection)
  {
    var testServices = new ServiceCollection();
    testServices.AddRange(serviceCollection);
    testServices.Replace(ServiceDescriptor.Singleton<DocumentModelStore, TestDocumentModelStore>());
    testServices.Replace(ServiceDescriptor.Singleton<IBrowserBridge, TestBrowserBridge>());
    testServices.Replace(ServiceDescriptor.Singleton<IAccountService, TestAccountService>());
    testServices.Replace(ServiceDescriptor.Singleton<ICommitter, TestCommitter>());
    testServices.Replace(ServiceDescriptor.Singleton<ISendConversionCache, TestSendConversionCache>());
    testServices.Replace(ServiceDescriptor.Singleton<ISqLiteJsonCacheManagerFactory, TestSqLiteJsonCacheManagerFactory>());
    testServices.Replace(ServiceDescriptor.Singleton<IConfigStorage, TestConfigStorage>());
    testServices.Replace(ServiceDescriptor.Singleton<ISerializeProcessFactory, TestSerializeProcessFactory>());
    var serviceProvider = testServices.BuildServiceProvider();
    SpeckleXunitTestFramework.ServiceProvider =  serviceProvider;
  }
}

public sealed class TestSerializeProcessFactory (
  IBaseChildFinder baseChildFinder,
  IObjectSerializerFactory objectSerializerFactory,
  ISqLiteJsonCacheManagerFactory sqLiteJsonCacheManagerFactory,
  IServerObjectManagerFactory serverObjectManagerFactory
) : ISerializeProcessFactory
{
  public ISerializeProcess CreateSerializeProcess(
    Uri url,
    string streamId,
    string? authorizationToken,
    IProgress<ProgressArgs>? progress,
    SerializeProcessOptions? options = null
  )
  {
    var sqLiteJsonCacheManager = sqLiteJsonCacheManagerFactory.CreateFromStream(streamId);
    var serverObjectManager = serverObjectManagerFactory.Create(url, streamId, authorizationToken);
    return new SerializeProcess(
      progress,
      sqLiteJsonCacheManager,
      serverObjectManager,
      baseChildFinder,
      objectSerializerFactory,
      new SerializeProcessOptions()
      {
        SkipServer = true,
        SkipCacheRead = true,
        SkipFindTotalObjects = true
      }
    );
  }

  public IDeserializeProcess CreateDeserializeProcess(Uri url, string streamId, string? authorizationToken, IProgress<ProgressArgs>? progress,
    DeserializeProcessOptions? options = null) =>
    throw new NotImplementedException();
}

public sealed class TestConfigStorage : IConfigStorage
{
  public string? GetConfig() => null;

  public void UpdateConfig(string config) { }

  public string? GetAccounts() => null;

  public void UpdateAccounts(string config) { }
}

public sealed class TestSqLiteJsonCacheManager(ISqLiteJsonCacheManager wrapped) : ISqLiteJsonCacheManager
{
  public void Dispose()
  {
    // TODO release managed resources here
  }

  public IReadOnlyCollection<(string Id, string Json)> GetAllObjects() => wrapped.GetAllObjects();

  public void DeleteObject(string id) => wrapped.DeleteObject(id);

  public string? GetObject(string id) => wrapped.GetObject(id);

  public void SaveObject(string id, string json) => wrapped.SaveObject(id, json);

  public void UpdateObject(string id, string json) => wrapped.UpdateObject(id, json);

  public void SaveObjects(IEnumerable<(string id, string json)> items) => wrapped.SaveObjects(items);

  public bool HasObject(string objectId) => wrapped.HasObject(objectId);
}

public sealed class TestSqLiteJsonCacheManagerFactory : ISqLiteJsonCacheManagerFactory, IDisposable
{
  private SqLiteJsonCacheManager _sqLiteJsonCacheManager = new ("Data Source=:memory:", 1);

  public ISqLiteJsonCacheManager CreateForUser(string scope) => new TestSqLiteJsonCacheManager(_sqLiteJsonCacheManager);

  public ISqLiteJsonCacheManager CreateFromStream(string streamId) => new TestSqLiteJsonCacheManager(_sqLiteJsonCacheManager);

  public void Dispose() => _sqLiteJsonCacheManager.Dispose();

  public void Initialize(string path)
  {
    _sqLiteJsonCacheManager = new($"Data Source={path}", 1);
  }
}

public class TestAccountService : IAccountService
{
  public Account GetAccountWithServerUrlFallback(string accountId, Uri serverUrl)
  {
    return new Account() { id = accountId, token = "token", userInfo = new UserInfo() { name = "test", email = "test@test.com" } };
  }
}

public class TestCommitter : ICommitter
{
  public Task Commit(Account account, SerializeProcessResults sendResult, SendInfo sendInfo,
    CancellationToken ct = default) => Task.CompletedTask;
}

public class TestSendConversionCache : ISendConversionCache
{
  public void StoreSendResult(string projectId, IReadOnlyDictionary<Id, ObjectReference> convertedReferences) { }

  public void EvictObjects(IEnumerable<string> objectIds) => throw new NotImplementedException();

  public void ClearCache() { }

  public bool TryGetValue(string projectId, string applicationId,
    [NotNullWhen(true)] out ObjectReference? objectReference)
  {
    objectReference = null;
    return false;
  }
}
