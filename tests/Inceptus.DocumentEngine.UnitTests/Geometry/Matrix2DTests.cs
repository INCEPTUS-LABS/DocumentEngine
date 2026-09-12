using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.UnitTests.Geometry;

public sealed class Matrix2DTests
{
    [Fact]
    public void IdentityDoesNotChangePointsOrVectors()
    {
        var point = new PointD(3d, -4d);
        var vector = new VectorD(5d, 6d);

        Assert.Equal(point, Matrix2D.Identity.TransformPoint(point));
        Assert.Equal(vector, Matrix2D.Identity.TransformVector(vector));
    }

    [Fact]
    public void TranslationAffectsPointsButNotVectors()
    {
        var translation = Matrix2D.CreateTranslation(10d, -5d);

        Assert.Equal(new PointD(12d, -2d), translation.TransformPoint(new PointD(2d, 3d)));
        Assert.Equal(new VectorD(2d, 3d), translation.TransformVector(new VectorD(2d, 3d)));
    }

    [Fact]
    public void ThenAppliesTheCurrentMatrixBeforeTheNextMatrix()
    {
        var scale = Matrix2D.CreateScale(2d, 3d);
        var translation = Matrix2D.CreateTranslation(5d, 7d);
        var point = new PointD(2d, 4d);

        var composed = scale.Then(translation);

        Assert.Equal(
            translation.TransformPoint(scale.TransformPoint(point)),
            composed.TransformPoint(point));
        Assert.Equal(new PointD(9d, 19d), composed.TransformPoint(point));
        Assert.NotEqual(
            scale.Then(translation).TransformPoint(point),
            translation.Then(scale).TransformPoint(point));
    }

    [Fact]
    public void AffineCompositionSupportsRotationAndShear()
    {
        var shear = new Matrix2D(1d, 0.25d, 0.5d, 1d, 0d, 0d);
        var rotation = Matrix2D.CreateRotation(Math.PI / 3d);
        var point = new PointD(3d, -2d);

        var expected = rotation.TransformPoint(shear.TransformPoint(point));
        var actual = shear.Then(rotation).TransformPoint(point);

        AssertPointApproximately(expected, actual);
    }

    [Fact]
    public void SingularAndNearSingularMatricesAreRejected()
    {
        var singular = new Matrix2D(1d, 2d, 2d, 4d, 0d, 0d);
        var nearSingular = new Matrix2D(1d, 0d, 0d, 1e-14, 0d, 0d);

        Assert.False(singular.TryInvert(out _));
        Assert.Throws<InvalidOperationException>(() => singular.Invert());
        Assert.False(nearSingular.TryInvert(out _));
    }

    [Fact]
    public void ScaleRelativeInversionAcceptsWellConditionedMagnitudes()
    {
        var small = Matrix2D.CreateScale(1e-200, 1e-200);
        var large = Matrix2D.CreateScale(1e200, 1e200);

        Assert.True(small.TryInvert(out var smallInverse));
        Assert.True(large.TryInvert(out var largeInverse));
        AssertPointApproximately(
            new PointD(2d, -3d),
            smallInverse.TransformPoint(small.TransformPoint(new PointD(2d, -3d))));
        AssertPointApproximately(
            new PointD(2d, -3d),
            largeInverse.TransformPoint(large.TransformPoint(new PointD(2d, -3d))));
    }

    [Fact]
    public void InversionRejectsNonFiniteResults()
    {
        var tooSmallToInvertIntoFiniteCoefficients = Matrix2D.CreateScale(double.Epsilon, double.Epsilon);

        Assert.False(tooSmallToInvertIntoFiniteCoefficients.TryInvert(out _));
    }

    [Fact]
    public void InverseRoundTripsPointsAndVectorsWithinTolerance()
    {
        var transform = new Matrix2D(1.5d, 0.2d, -0.4d, 2.1d, 100d, -50d)
            .Then(Matrix2D.CreateRotation(0.37d));
        var inverse = transform.Invert();
        var point = new PointD(12.25d, -8.75d);
        var vector = new VectorD(-3.5d, 6.25d);

        AssertPointApproximately(point, inverse.TransformPoint(transform.TransformPoint(point)));
        AssertVectorApproximately(vector, inverse.TransformVector(transform.TransformVector(vector)));
    }

    [Fact]
    public void InversionToleranceMustBeFiniteAndNonNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Matrix2D.Identity.TryInvert(out _, -1d));
        Assert.Throws<ArgumentOutOfRangeException>(() => Matrix2D.Identity.TryInvert(out _, double.NaN));
    }

    private static void AssertPointApproximately(PointD expected, PointD actual)
    {
        AssertApproximately(expected.X, actual.X);
        AssertApproximately(expected.Y, actual.Y);
    }

    private static void AssertVectorApproximately(VectorD expected, VectorD actual)
    {
        AssertApproximately(expected.X, actual.X);
        AssertApproximately(expected.Y, actual.Y);
    }

    private static void AssertApproximately(double expected, double actual)
    {
        const double absoluteTolerance = 1e-12;
        const double relativeTolerance = 1e-12;
        var tolerance = absoluteTolerance +
            (relativeTolerance * Math.Max(Math.Abs(expected), Math.Abs(actual)));

        Assert.True(
            Math.Abs(expected - actual) <= tolerance,
            $"Expected {expected:R}, actual {actual:R}, tolerance {tolerance:R}.");
    }
}

