using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Speckle.Converters.Common.Registration;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.Common.Tests;

public class ConverterManagerTests
{
  private sealed class TestConverter
  {
    public string TestString { get; set; }
  }

  private ConverterManager<object> SetupManager(string testString, Type targetType)
  {
    var serviceCollection = new ServiceCollection();
    serviceCollection.AddTransient<TestConverter>();
    var converterTypes = new ConcurrentDictionary<string, Type>();
    converterTypes.TryAdd(testString, targetType);

    var sut = new ConverterManager<object>(converterTypes, serviceCollection.BuildServiceProvider());

    return sut;
  }

  [Test]
  public void Test_Null()
  {
    var sut = SetupManager("Test", typeof(TestConverter));
    Assert.Throws<ConversionNotSupportedException>(() => sut.ResolveConverter(typeof(string), false));
  }

  [Test]
  public void Test_NoFallback()
  {
    var sut = SetupManager("String", typeof(TestConverter));
    var converter = sut.ResolveConverter(typeof(string), false);
    converter.Should().NotBeNull();
  }

  [Test]
  public void Test_Fallback()
  {
    var sut = SetupManager("Object", typeof(TestConverter));
    var converter = sut.ResolveConverter(typeof(string), true);
    converter.Should().NotBeNull();
  }

  [Test]
  public void Test_Fallback_Null()
  {
    var sut = SetupManager("Object", typeof(TestConverter));
    Assert.Throws<ConversionNotSupportedException>(() => sut.ResolveConverter(typeof(string), false));
  }
}
