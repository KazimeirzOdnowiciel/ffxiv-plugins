namespace Mountbook.Tests;

/// <summary>
/// A deliberately failing fact, committed for one run only, to prove that the CI
/// gate fails the build on a red test instead of reporting success.
/// </summary>
public sealed class RedGateProbe
{
    [Xunit.Fact]
    public void ThisTestIsRedOnPurpose() => Xunit.Assert.True(false, "red gate probe");
}
