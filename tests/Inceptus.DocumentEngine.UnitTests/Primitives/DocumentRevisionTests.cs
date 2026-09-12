using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.UnitTests.Primitives;

public sealed class DocumentRevisionTests
{
    [Fact]
    public void ZeroIsTheInitialRevision()
    {
        Assert.Equal(default, DocumentRevision.Zero);
        Assert.Equal(0UL, DocumentRevision.Zero.Value);
        Assert.Equal("0", DocumentRevision.Zero.ToString());
    }

    [Fact]
    public void IncrementReturnsTheNextRevisionWithoutMutatingTheOriginal()
    {
        var original = new DocumentRevision(41UL);

        var next = original.Increment();

        Assert.Equal(41UL, original.Value);
        Assert.Equal(42UL, next.Value);
        Assert.True(original < next);
        Assert.True(original <= next);
        Assert.True(next > original);
        Assert.True(next >= original);
        Assert.True(original.CompareTo(next) < 0);
    }

    [Fact]
    public void IncrementThrowsOnOverflow()
    {
        var maximum = new DocumentRevision(ulong.MaxValue);

        Assert.Throws<OverflowException>(() => maximum.Increment());
    }

    [Fact]
    public void StringRepresentationIsDeterministic()
    {
        var revision = new DocumentRevision(18_446_744_073_709_551_615UL);

        Assert.Equal("18446744073709551615", revision.ToString());
    }
}

