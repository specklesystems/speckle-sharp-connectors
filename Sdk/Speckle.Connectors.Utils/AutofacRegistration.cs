using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Autofac;
using Autofac.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Speckle.Connectors.Utils;

/// <summary>
/// Extension methods for registering ASP.NET Core dependencies with Autofac.
/// </summary>
internal static class AutofacRegistration
{

  public static void Populate(
    this ContainerBuilder builder,
    IEnumerable<ServiceDescriptor> descriptors)
  {
    Populate(builder, descriptors, null);
  }

  public static void Populate(
    this ContainerBuilder builder,
    IEnumerable<ServiceDescriptor> descriptors,
    object? lifetimeScopeTagForSingletons)
  {
    if (descriptors == null)
    {
      throw new ArgumentNullException(nameof(descriptors));
    }


    Register(builder, descriptors, lifetimeScopeTagForSingletons!);
  }

  /// <summary>
  /// Configures the lifecycle on a service registration.
  /// </summary>
  /// <typeparam name="TActivatorData">The activator data type.</typeparam>
  /// <typeparam name="TRegistrationStyle">The object registration style.</typeparam>
  /// <param name="registrationBuilder">The registration being built.</param>
  /// <param name="lifecycleKind">The lifecycle specified on the service registration.</param>
  /// <param name="lifetimeScopeTagForSingleton">
  /// If not <see langword="null"/> then all registrations with lifetime <see cref="ServiceLifetime.Singleton" /> are registered
  /// using <see cref="IRegistrationBuilder{TLimit,TActivatorData,TRegistrationStyle}.InstancePerMatchingLifetimeScope" />
  /// with provided <paramref name="lifetimeScopeTagForSingleton"/>
  /// instead of using <see cref="IRegistrationBuilder{TLimit,TActivatorData,TRegistrationStyle}.SingleInstance"/>.
  /// </param>
  /// <returns>
  /// The <paramref name="registrationBuilder" />, configured with the proper lifetime scope,
  /// and available for additional configuration.
  /// </returns>
  private static IRegistrationBuilder<object, TActivatorData, TRegistrationStyle> ConfigureLifecycle<TActivatorData, TRegistrationStyle>(
    this IRegistrationBuilder<object, TActivatorData, TRegistrationStyle> registrationBuilder,
    ServiceLifetime lifecycleKind,
    object lifetimeScopeTagForSingleton)
  {
    switch (lifecycleKind)
    {
      case ServiceLifetime.Singleton:
        if (lifetimeScopeTagForSingleton == null)
        {
          registrationBuilder.SingleInstance();
        }
        else
        {
          registrationBuilder.InstancePerMatchingLifetimeScope(lifetimeScopeTagForSingleton);
        }

        break;
      case ServiceLifetime.Scoped:
        registrationBuilder.InstancePerLifetimeScope();
        break;
      case ServiceLifetime.Transient:
        registrationBuilder.InstancePerDependency();
        break;
    }

    return registrationBuilder;
  }

  /// <summary>
  /// Populates the Autofac container builder with the set of registered service descriptors.
  /// </summary>
  /// <param name="builder">
  /// The <see cref="ContainerBuilder"/> into which the registrations should be made.
  /// </param>
  /// <param name="descriptors">
  /// The set of service descriptors to register in the container.
  /// </param>
  /// <param name="lifetimeScopeTagForSingletons">
  /// If not <see langword="null"/> then all registrations with lifetime <see cref="ServiceLifetime.Singleton" /> are registered
  /// using <see cref="IRegistrationBuilder{TLimit,TActivatorData,TRegistrationStyle}.InstancePerMatchingLifetimeScope" />
  /// with provided <paramref name="lifetimeScopeTagForSingletons"/>
  /// instead of using <see cref="IRegistrationBuilder{TLimit,TActivatorData,TRegistrationStyle}.SingleInstance"/>.
  /// </param>
  [SuppressMessage("CA2000", "CA2000", Justification = "Registrations created here are disposed when the built container is disposed.")]
  private static void Register(
    ContainerBuilder builder,
    IEnumerable<ServiceDescriptor> descriptors,
    object lifetimeScopeTagForSingletons)
  {
    foreach (var descriptor in descriptors)
    {
      if (descriptor.ImplementationType != null)
      {
        // Test if the an open generic type is being registered
        var serviceTypeInfo = descriptor.ServiceType.GetTypeInfo();
        if (serviceTypeInfo.IsGenericTypeDefinition)
        {
          builder
            .RegisterGeneric(descriptor.ImplementationType)
            .As(descriptor.ServiceType)
            .ConfigureLifecycle(descriptor.Lifetime, lifetimeScopeTagForSingletons);
        }
        else
        {
          builder
            .RegisterType(descriptor.ImplementationType)
            .As(descriptor.ServiceType)
            .ConfigureLifecycle(descriptor.Lifetime, lifetimeScopeTagForSingletons);
        }
      }
      else if (descriptor.ImplementationFactory != null)
      {
        var registration = RegistrationBuilder.ForDelegate(descriptor.ServiceType, (context, parameters) =>
          {
            var serviceProvider = context.Resolve<IServiceProvider>();
            return descriptor.ImplementationFactory(serviceProvider);
          })
          .ConfigureLifecycle(descriptor.Lifetime, lifetimeScopeTagForSingletons)
          .CreateRegistration();

        builder.RegisterComponent(registration);
      }
      else
      {
        builder
          .RegisterInstance(descriptor.ImplementationInstance)
          .As(descriptor.ServiceType)
          .ConfigureLifecycle(descriptor.Lifetime, null!);
      }
    }
  }
}
