using System.Collections;

using PyDotNet.Exceptions;
using PyDotNet.Native;
using PyDotNet.Types;

namespace PyDotNet.Iterators;

/// <summary>
/// Bridges a Python iterable to .NET's <see cref="IEnumerable{T}"/> of <see cref="PyObject"/>.
/// Each yielded item is a new reference — callers are responsible for disposing it.
/// </summary>
/// <example>
/// <code>
/// foreach (var item in PyIterator.From(pyList))
/// using (item)
/// {
///     Console.WriteLine(item.As&lt;int&gt;());
/// }
/// </code>
/// </example>
public static class PyIterator
{
    /// <summary>
    /// Creates an <see cref="IEnumerable{T}"/> that iterates over <paramref name="iterable"/>
    /// using the Python iterator protocol (<c>__iter__</c> / <c>__next__</c>).
    /// </summary>
    /// <param name="iterable">Any Python object that supports <c>__iter__</c>.</param>
    /// <exception cref="PyInteropException">Thrown if the object is not iterable.</exception>
    public static IEnumerable<PyObject> From(PyObject iterable)
    {
        ArgumentNullException.ThrowIfNull(iterable);

        IntPtr iterHandle;
        {
            using var gil = new GilScope();
            iterHandle = NativeMethods.PyObject_GetIter(iterable.Handle);
            if (iterHandle == IntPtr.Zero)
            {
                PythonException.ThrowIfPythonErrorOccurred();
                throw new PyInteropException("Object is not iterable (__iter__ not found).");
            }
        }

        return IterateCore(iterHandle);
    }

    private static IEnumerable<PyObject> IterateCore(IntPtr iterHandle)
    {
        try
        {
            while (true)
            {
                IntPtr next;
                {
                    using var gil = new GilScope();
                    next = NativeMethods.PyIter_Next(iterHandle);
                    if (next == IntPtr.Zero)
                    {
                        if (NativeMethods.PyErr_Occurred() != IntPtr.Zero)
                        {
                            PythonException.ThrowIfPythonErrorOccurred();
                        }

                        yield break; // normal exhaustion (StopIteration)
                    }
                }

                yield return PyObject.FromNewReference(next);
            }
        }
        finally
        {
            using var gilFinal = new GilScope();
            NativeMethods.Py_DecRef(iterHandle);
        }
    }
}