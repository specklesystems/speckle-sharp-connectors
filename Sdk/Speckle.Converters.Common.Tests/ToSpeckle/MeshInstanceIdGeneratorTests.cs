using NUnit.Framework;
using Speckle.Converters.Common.ToSpeckle;

namespace Speckle.Converters.Common.Tests.ToSpeckle;

public class MeshInstanceIdGeneratorTests
{
  private static IEnumerable<List<double>> TestCases()
  {
    int[] testCases = [0, 1, 100, 1_000_000];
    foreach (int testLength in testCases)
    {
      yield return Enumerable
        .Range(0, testLength)
        .Select(_ => TestContext.CurrentContext.Random.NextDouble(float.MinValue, float.MaxValue))
        .ToList();
    }
  }

  [Test]
  [TestCaseSource(nameof(TestCases))]
  public void TestEquivalentImplementations(List<double> vertices)
  {
    var result = MeshInstanceIdGenerator.GenerateUntransformedMeshId(vertices);
    var resultSpan = MeshInstanceIdGenerator.GenerateUntransformedMeshId_Span(vertices);

    Assert.That(result, Is.EqualTo(resultSpan));
  }
}
