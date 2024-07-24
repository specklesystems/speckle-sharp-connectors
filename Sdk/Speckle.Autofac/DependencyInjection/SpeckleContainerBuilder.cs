using System.Reflection;
using Autofac;
using Speckle.Autofac.Files;
using Module = Autofac.Module;

namespace Speckle.Autofac.DependencyInjection;

public class SpeckleContainerBuilder
{
  private readonly struct SpeckleContainerContext : ISpeckleContainerContext
  {
    private readonly IComponentContext _componentContext;

    public SpeckleContainerContext(IComponentContext componentContext)
    {
      _componentContext = componentContext;
    }

    public T Resolve<T>()
      where T : notnull => _componentContext.Resolve<T>();
  }

  private static readonly Type s_moduleType = typeof(ISpeckleModule);
  private readonly IStorageInfo _storageInfo;

  private SpeckleContainerBuilder(IStorageInfo storageInfo, ContainerBuilder? containerBuilder)
  {
    _storageInfo = storageInfo;
    ContainerBuilder = containerBuilder ?? new ContainerBuilder();
  }

  public static SpeckleContainerBuilder CreateInstance() => new(new StorageInfo(), null);

  private static SpeckleContainerBuilder CreateInstanceForLoading(ContainerBuilder containerBuilder) =>
    new(new StorageInfo(), containerBuilder);

  // POC: HOW TO GET TYPES loaded, this feels a bit heavy handed and relies on Autofac where we can probably do something different
  public SpeckleContainerBuilder LoadAutofacModules(Assembly pluginAssembly, IEnumerable<string> dependencyPaths)
  {
    // look for assemblies in these paths that offer autofac modules
    foreach (string path in dependencyPaths)
    {
      // POC: naming conventions
      // find assemblies
      var assembliesInPath = _storageInfo.GetFilenamesInDirectory(path, "Speckle*.dll");
      var assemblies = assembliesInPath.Select(LoadAssemblyFile).ToList();
      if (assemblies.All(x => x != pluginAssembly))
      {
        LoadAssembly(pluginAssembly);
      }
    }

    return this;
  }

  private Assembly? LoadAssemblyFile(string file)
  {
    try
    {
      // inspect the assemblies for Autofac.Module
      var assembly = Assembly.LoadFrom(file);
      LoadAssembly(assembly);
      return assembly;
    }
    // POC: catch only certain exceptions
    catch (Exception ex) when (!ex.IsFatal())
    {
      return null;
    }
  }

  private void LoadAssembly(Assembly assembly)
  {
    var moduleClasses = assembly.GetTypes().Where(x => x.GetInterfaces().Contains(s_moduleType)).ToList();

    // create each module
    // POC: could look for some attribute here
    foreach (var moduleClass in moduleClasses)
    {
      var module = (ISpeckleModule)Activator.CreateInstance(moduleClass);
      ContainerBuilder.RegisterModule(new ModuleAdapter(module));
    }
  }

  private readonly Lazy<IReadOnlyList<Type>> _types =
    new(() =>
    {
      var types = new List<Type>();
      foreach (
        var asm in AppDomain
          .CurrentDomain.GetAssemblies()
          .Where(x => x.GetName().Name.StartsWith("Speckle", StringComparison.OrdinalIgnoreCase))
      )
      {
        types.AddRange(asm.GetTypes());
      }
      return types;
    });

  public IReadOnlyList<Type> SpeckleTypes => _types.Value;
  public ContainerBuilder ContainerBuilder { get; }

  private sealed class ModuleAdapter : Module
  {
    private readonly ISpeckleModule _speckleModule;

    public ModuleAdapter(ISpeckleModule speckleModule)
    {
      _speckleModule = speckleModule;
    }

    protected override void Load(ContainerBuilder builder) => _speckleModule.Load(CreateInstanceForLoading(builder));
  }

  public SpeckleContainerBuilder AddModule(ISpeckleModule module)
  {
    ContainerBuilder.RegisterModule(new ModuleAdapter(module));

    return this;
  }

  public SpeckleContainerBuilder AddSingleton<T>(T instance)
    where T : class
  {
    ContainerBuilder.RegisterInstance(instance).SingleInstance();
    return this;
  }

  public SpeckleContainerBuilder AddSingleton<T>()
    where T : class
  {
    ContainerBuilder.RegisterType<T>().AsSelf().SingleInstance();
    return this;
  }

  public SpeckleContainerBuilder AddSingletonInstance<T>()
    where T : class
  {
    ContainerBuilder.RegisterType<T>().AsSelf().SingleInstance().AutoActivate();
    return this;
  }

  public SpeckleContainerBuilder AddSingletonInstance<TInterface, T>()
    where T : class, TInterface
    where TInterface : notnull
  {
    ContainerBuilder.RegisterType<T>().As<TInterface>().SingleInstance().AutoActivate();
    return this;
  }

  public SpeckleContainerBuilder AddSingleton<TInterface, T>()
    where T : class, TInterface
    where TInterface : notnull
  {
    ContainerBuilder.RegisterType<T>().As<TInterface>().SingleInstance();
    return this;
  }

  public SpeckleContainerBuilder AddSingleton<TInterface, T>(string param, string value)
    where T : class, TInterface
    where TInterface : notnull
  {
    ContainerBuilder.RegisterType<T>().As<TInterface>().SingleInstance().WithParameter(param, value);
    return this;
  }

  public SpeckleContainerBuilder AddScoped<TInterface, T>()
    where T : class, TInterface
    where TInterface : notnull
  {
    ContainerBuilder.RegisterType<T>().As<TInterface>().InstancePerLifetimeScope();
    return this;
  }

  public SpeckleContainerBuilder AddScoped<T>(Func<ISpeckleContainerContext, T> action)
    where T : notnull
  {
    ContainerBuilder.Register<T>(c => action(new SpeckleContainerContext(c))).InstancePerLifetimeScope();
    return this;
  }

  public SpeckleContainerBuilder AddScoped<T>()
    where T : class
  {
    ContainerBuilder.RegisterType<T>().AsSelf().InstancePerLifetimeScope();
    return this;
  }

  public SpeckleContainerBuilder AddTransient<TInterface, T>()
    where T : class, TInterface
    where TInterface : notnull
  {
    ContainerBuilder.RegisterType<T>().As<TInterface>().InstancePerDependency();
    return this;
  }

  public SpeckleContainerBuilder AddTransient<T>()
    where T : class
  {
    ContainerBuilder.RegisterType<T>().AsSelf().InstancePerDependency();
    return this;
  }

  public SpeckleContainerBuilder AddTransient<T>(Func<ISpeckleContainerContext, T> action)
    where T : notnull
  {
    ContainerBuilder.Register<T>(c => action(new SpeckleContainerContext(c))).InstancePerDependency();
    return this;
  }

  /// <summary>
  /// Scans the assembly.
  /// Scan matches classes with interfaces that match Iclass and registers them as Transient with the interface.
  /// Do this when scoping isn't known but all types should be registered for DI.
  /// </summary>
  public SpeckleContainerBuilder ScanAssembly(Assembly assembly)
  {
    ContainerBuilder
      .RegisterAssemblyTypes(assembly)
      .Where(t => t.IsClass)
      .As(GetInterfacesWithNameName)
      .InstancePerDependency();
    return this;
  }

  /// <summary>
  /// Scans the assembly containing the type T.
  /// Scan matches classes with interfaces that match Iclass and registers them as Transient with the interface.
  /// Do this when scoping isn't known but all types should be registered for DI.
  /// </summary>
  /// <typeparam name="T"></typeparam>
  /// <returns></returns>
  public SpeckleContainerBuilder ScanAssemblyOfType<T>() => ScanAssembly(typeof(T).Assembly);

  private static IEnumerable<Type> GetInterfacesWithNameName(Type type) =>
    type.GetInterfaces().Where(i => i.Name == "I" + type.Name);

  public SpeckleContainer Build()
  {
    var container = ContainerBuilder.Build();
    return new SpeckleContainer(container);
  }
}
