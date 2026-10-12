using FluentAssertions;
using NUnit.Framework;
using Speckle.Connectors.Logging.Internal;

namespace Speckle.Connectors.Common.Tests.Logging;

public class OtlpHeadersTests
{
  [Test]
  public void Join_TwoHeaders_SeparatesThemWithAComma()
  {
    var headers = new Dictionary<string, string> { ["authorization"] = "Bearer abc", ["x-tenant"] = "speckle" };

    OtlpHeaders.Join(headers).Should().Be("authorization=Bearer abc,x-tenant=speckle");
  }

  [Test]
  public void Join_OneHeader_HasNoSeparator()
  {
    OtlpHeaders
      .Join(new Dictionary<string, string> { ["authorization"] = "Bearer abc" })
      .Should()
      .Be("authorization=Bearer abc");
  }

  [Test]
  public void Join_NullOrEmpty_IsEmpty()
  {
    OtlpHeaders.Join(null).Should().BeEmpty();
    OtlpHeaders.Join(new Dictionary<string, string>()).Should().BeEmpty();
  }
}
