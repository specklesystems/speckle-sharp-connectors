using Microsoft.Extensions.DependencyInjection;
using Rhino;
using Rhino.Runtime.InProcess;
using Speckle.Connectors.Common;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.Rhino.DependencyInjection;
using Speckle.Converters.Rhino;

namespace Speckle.Importers.Rhino;

public static class Program
{
  static Program()
  {
    Resolver.Initialize();
  }

  [STAThread]
  public static async Task Main()
  {
    try
    {
      using (new RhinoCore([], WindowStyle.NoWindow))
      {
        using var doc = RhinoDoc.Open("C:\\Users\\adam\\Downloads\\objects.dwg", out _);
        var services = new ServiceCollection();
       // var path = Path.Combine(RhinoApp.GetExecutableDirectory().FullName, "Rhino.exe");
        services.Initialize(HostApplications.Rhino, HostAppVersion.v2026);
        services.AddRhino(false);
        services.AddRhinoConverters();
        //override default
        services.AddSingleton<IThreadContext>(new ImporterThreadContext());

        // but the Rhino connector has `.rhp` as it is extension.
        var container = services.BuildServiceProvider();
        var sender = ActivatorUtilities.CreateInstance<Sender>(container);
        await sender.Send();
      }
    }
    catch (Exception ex)
    {
      Console.WriteLine(ex);
      throw;
    }
  }
}

public class ImporterThreadContext : ThreadContext
{
  protected override Task<T> WorkerToMainAsync<T>(Func<Task<T>> action)
  {
    var t = Task.Factory.StartNew(action, default, TaskCreationOptions.AttachedToParent, TaskScheduler.Default);
    return t.Unwrap();
  }

  protected override Task<T> MainToWorkerAsync<T>(Func<Task<T>> action)
  {
    Task<Task<T>> f = Task.Factory.StartNew(
      action,
      default,
      TaskCreationOptions.AttachedToParent,
      TaskScheduler.Default
    );
    return f.Unwrap();
  }

  protected override Task<T> WorkerToMain<T>(Func<T> action)
  {
    var t = Task.Factory.StartNew(action, default, TaskCreationOptions.AttachedToParent, TaskScheduler.Default);
    return t;
  }

  protected override Task<T> MainToWorker<T>(Func<T> action)
  {
    Task<T> f = Task.Factory.StartNew(action, default, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    return f;
  }
}
