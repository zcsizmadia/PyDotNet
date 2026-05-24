using PyDotNet.Types;

namespace PyDotNet.NumPy;

/// <summary>
/// Provides access to NumPy's legacy random-number generation API (<c>numpy.random</c>).
/// Instances are created and owned by <see cref="NumpyModule"/>; do not dispose them independently.
/// </summary>
public sealed class NumpyRandom
{
    // Not owned — NumpyModule disposes _random when it is itself disposed.
    private readonly PyObject _random;

    internal NumpyRandom(PyObject random)
    {
        _random = random;
    }

    /// <summary>
    /// Sets the random seed for reproducible results (<c>numpy.random.seed</c>).
    /// </summary>
    public void Seed(int seed)
    {
        using var fn = _random.GetAttr("seed");
        using var result = fn.Call(seed);
    }

    /// <summary>
    /// Draws samples from a normal (Gaussian) distribution.
    /// Equivalent to <c>numpy.random.normal(loc, scale, size=shape)</c>.
    /// </summary>
    /// <param name="shape">Shape of the output array.</param>
    /// <param name="loc">Mean of the distribution (default 0).</param>
    /// <param name="scale">Standard deviation (default 1).</param>
    public NdArray Normal(long[] shape, double loc = 0.0, double scale = 1.0)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var kwargs = new Dictionary<string, object?>
        {
            ["loc"]   = loc,
            ["scale"] = scale,
            ["size"]  = shape,
        };
        using var fn = _random.GetAttr("normal");
        return new NdArray(fn.Call(Array.Empty<object?>(), kwargs));
    }

    /// <summary>
    /// Draws samples from a uniform distribution over [<paramref name="low"/>, <paramref name="high"/>).
    /// Equivalent to <c>numpy.random.uniform(low, high, size=shape)</c>.
    /// </summary>
    /// <param name="shape">Shape of the output array.</param>
    /// <param name="low">Lower boundary (default 0).</param>
    /// <param name="high">Upper boundary (default 1).</param>
    public NdArray Uniform(long[] shape, double low = 0.0, double high = 1.0)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var kwargs = new Dictionary<string, object?>
        {
            ["low"]  = low,
            ["high"] = high,
            ["size"] = shape,
        };
        using var fn = _random.GetAttr("uniform");
        return new NdArray(fn.Call(Array.Empty<object?>(), kwargs));
    }

    /// <summary>
    /// Draws random integers from [<paramref name="low"/>, <paramref name="high"/>).
    /// Equivalent to <c>numpy.random.randint(low, high, size=shape)</c>.
    /// </summary>
    /// <param name="low">Lowest integer to draw (inclusive).</param>
    /// <param name="high">One above the highest integer to draw (exclusive).</param>
    /// <param name="shape">Shape of the output array.</param>
    public NdArray Integers(long low, long high, long[] shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var kwargs = new Dictionary<string, object?>
        {
            ["low"]  = low,
            ["high"] = high,
            ["size"] = shape,
        };
        using var fn = _random.GetAttr("randint");
        return new NdArray(fn.Call(Array.Empty<object?>(), kwargs));
    }

    /// <summary>
    /// Draws samples from the standard normal distribution (mean 0, std 1).
    /// Equivalent to <c>numpy.random.standard_normal(size=shape)</c>.
    /// </summary>
    /// <param name="shape">Shape of the output array.</param>
    public NdArray Standard(long[] shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var kwargs = new Dictionary<string, object?> { ["size"] = shape };
        using var fn = _random.GetAttr("standard_normal");
        return new NdArray(fn.Call(Array.Empty<object?>(), kwargs));
    }

    /// <summary>
    /// Draws samples from an exponential distribution.
    /// Equivalent to <c>numpy.random.exponential(scale, size=shape)</c>.
    /// </summary>
    /// <param name="shape">Shape of the output array.</param>
    /// <param name="scale">Scale (inverse rate) of the distribution (default 1.0).</param>
    public NdArray Exponential(long[] shape, double scale = 1.0)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var kwargs = new Dictionary<string, object?>
        {
            ["scale"] = scale,
            ["size"]  = shape,
        };
        using var fn = _random.GetAttr("exponential");
        return new NdArray(fn.Call(Array.Empty<object?>(), kwargs));
    }

    /// <summary>
    /// Draws samples from a Poisson distribution.
    /// Equivalent to <c>numpy.random.poisson(lam, size=shape)</c>.
    /// </summary>
    /// <param name="shape">Shape of the output array.</param>
    /// <param name="lam">Expected number of events (λ, default 1.0).</param>
    public NdArray Poisson(long[] shape, double lam = 1.0)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var kwargs = new Dictionary<string, object?>
        {
            ["lam"]  = lam,
            ["size"] = shape,
        };
        using var fn = _random.GetAttr("poisson");
        return new NdArray(fn.Call(Array.Empty<object?>(), kwargs));
    }

    /// <summary>
    /// Generates random samples from <c>arange(n)</c>.
    /// Equivalent to <c>numpy.random.choice(n, size=shape, replace=replace)</c>.
    /// </summary>
    /// <param name="n">Population size; samples are drawn from [0, n).</param>
    /// <param name="shape">Shape of the output array.</param>
    /// <param name="replace">Whether to sample with replacement (default <see langword="true"/>).</param>
    public NdArray Choice(long n, long[] shape, bool replace = true)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var kwargs = new Dictionary<string, object?>
        {
            ["size"]    = shape,
            ["replace"] = replace,
        };
        using var fn = _random.GetAttr("choice");
        return new NdArray(fn.Call(new object?[] { n }, kwargs));
    }

    /// <summary>
    /// Returns a random permutation of integers in [0, <paramref name="n"/>).
    /// Equivalent to <c>numpy.random.permutation(n)</c>.
    /// </summary>
    public NdArray Permutation(long n)
    {
        using var fn = _random.GetAttr("permutation");
        return new NdArray(fn.Call(n));
    }
}
