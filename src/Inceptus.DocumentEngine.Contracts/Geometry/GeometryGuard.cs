namespace Inceptus.DocumentEngine.Contracts.Geometry;

internal static class GeometryGuard
{
    public static double RequireFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The value must be finite.");
        }

        return value;
    }

    public static double RequireNonNegative(double value, string parameterName)
    {
        RequireFinite(value, parameterName);

        if (value < 0d)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The value must be non-negative.");
        }

        return value;
    }

    public static void RequireFiniteSum(double first, double second, string parameterName)
    {
        if (!double.IsFinite(first + second))
        {
            throw new ArgumentOutOfRangeException(parameterName, "The derived coordinate must be finite.");
        }
    }
}

