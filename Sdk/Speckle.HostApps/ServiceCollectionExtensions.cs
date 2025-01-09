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
using Speckle.Sdk.Api;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Models;
using Speckle.Sdk.Serialisation;
using Speckle.Sdk.Serialisation.V2.Send;
using Speckle.Sdk.SQLite;

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
    testServices.Replace(ServiceDescriptor.Singleton<IOperations, TestOperations>());
    testServices.Replace(ServiceDescriptor.Singleton<IAccountService, TestAccountService>());
    testServices.Replace(ServiceDescriptor.Singleton<ICommitter, TestCommitter>());
    testServices.Replace(ServiceDescriptor.Singleton<ISendConversionCache, TestSendConversionCache>());
    testServices.Replace(ServiceDescriptor.Singleton<ISqLiteJsonCacheManagerFactory, TestSqLiteJsonCacheManagerFactory>());
    var serviceProvider = testServices.BuildServiceProvider();
    SpeckleXunitTestFramework.ServiceProvider =  serviceProvider;
  }
}


public sealed class TestSqLiteJsonCacheManagerFactory : ISqLiteJsonCacheManagerFactory, IDisposable
{
  private readonly SqLiteJsonCacheManager _sqLiteJsonCacheManager = 
    new SqLiteJsonCacheManager("Data Source=:memory;");

  public ISqLiteJsonCacheManager CreateForUser(string scope) => _sqLiteJsonCacheManager;

  public ISqLiteJsonCacheManager CreateFromStream(string streamId) => _sqLiteJsonCacheManager;

  public void Dispose() { }
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
