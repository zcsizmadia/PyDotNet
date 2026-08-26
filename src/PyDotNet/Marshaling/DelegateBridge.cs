using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using PyDotNet.Exceptions;
using PyDotNet.Native;

namespace PyDotNet.Marshaling;

/// <summary>
/// Exposes a .NET delegate to Python as an ordinary callable.
/// </summary>
/// <remarks>
/// <para>
/// The callable is a <c>PyCFunction</c> whose <c>self</c> is a capsule holding a
/// <see cref="GCHandle"/> to the delegate and everything derived from it. That one link
/// settles both hard parts at once. Lifetime: Python's own reference counting keeps the
/// delegate alive exactly as long as the callable is reachable, and the capsule's
/// destructor releases the handle and the unmanaged blocks when the last reference goes —
/// no registry to consult and nothing to unregister. Dispatch: the trampoline recovers the
/// delegate from the <c>self</c> it is handed, so a single static entry point serves every
/// callable without a lookup.
/// </para>
/// <para>
/// The GIL is held for the duration of the call, which is what Python guarantees any
/// callable it invokes. Releasing it around the delegate would let the callback observe a
/// half-mutated interpreter — <c>sorted(key=...)</c> is midway through a list when it calls
/// back — and a delegate that uses PyDotNet re-acquires the GIL anyway.
/// </para>
/// </remarks>
internal static unsafe class DelegateBridge
{
    private const int METH_VARARGS = 0x0001;
    private const int METH_KEYWORDS = 0x0002;

    /// <summary>
    /// CPython's <c>PyMethodDef</c>. Kept alive for as long as the callable, because
    /// <c>PyCFunction_NewEx</c> stores the pointer rather than copying the struct.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct PyMethodDef
    {
        internal IntPtr Name;
        internal IntPtr Method;
        internal int Flags;
        internal IntPtr Doc;
    }

    /// <summary>
    /// Everything one callable owns. Reached through the capsule, and released by its
    /// destructor.
    /// </summary>
    private sealed class CallbackContext
    {
        internal required Delegate Target { get; init; }

        /// <summary>Cached once, because they are read on every call.</summary>
        internal required ParameterInfo[] Parameters { get; init; }

        internal required bool ReturnsVoid { get; init; }

        /// <summary>
        /// Calls <see cref="Target"/> with the bound arguments. Shared by every callable of
        /// the same delegate type — see <see cref="GetInvoker"/>.
        /// </summary>
        internal required Func<Delegate, object?[], object?> Invoke { get; init; }

        /// <summary>Unmanaged blocks freed when the capsule is collected.</summary>
        internal IntPtr MethodDef { get; set; }

        internal IntPtr NameBlock { get; set; }
    }

    // "pydotnet_delegate\0", pinned: CPython stores the pointer and compares against it on
    // every capsule access.
    private static readonly byte[] _capsuleNameBytes = "pydotnet_delegate\0"u8.ToArray();
    private static readonly GCHandle _capsuleNamePin =
        GCHandle.Alloc(_capsuleNameBytes, GCHandleType.Pinned);

    /// <summary>Builtin exception types, resolved once each. Borrowed references.</summary>
    private static readonly ConcurrentDictionary<string, IntPtr> _builtinExceptions = new();

    /// <summary>One compiled invoker per delegate type. See <see cref="GetInvoker"/>.</summary>
    private static readonly ConcurrentDictionary<Type, Func<Delegate, object?[], object?>> _invokers = new();

    /// <summary>
    /// Wraps <paramref name="target"/> as a Python callable and returns a new reference.
    /// The caller holds the GIL.
    /// </summary>
    internal static IntPtr Create(Delegate target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var method = target.Method;
        var parameters = method.GetParameters();

        foreach (var parameter in parameters)
        {
            // by-ref parameters have no meaning on the Python side: there is nothing for
            // the callee to write back into. Rejecting at creation names the delegate,
            // rather than failing later inside a call with no obvious source.
            if (parameter.ParameterType.IsByRef)
            {
                throw new PyInteropException(
                    $"Cannot expose delegate '{DescribeTarget(target)}' to Python: parameter " +
                    $"'{parameter.Name}' is passed by reference, which has no Python equivalent.");
            }
        }

        var returnType = method.ReturnType;

        // Awaiting a .NET task from Python needs the asyncio bridge to drive it, which this
        // does not do. Converting the Task itself would fail later with a marshaling error
        // that says nothing about the real limitation.
        if (IsAwaitable(returnType))
        {
            throw new PyInteropException(
                $"Cannot expose delegate '{DescribeTarget(target)}' to Python: it returns " +
                $"'{returnType.Name}'. Asynchronous callbacks are not supported yet; return a " +
                "completed value, or drive the work to completion inside the delegate.");
        }

        var context = new CallbackContext
        {
            Target = target,
            Parameters = parameters,
            ReturnsVoid = returnType == typeof(void),
            Invoke = GetInvoker(target.GetType(), parameters, returnType),
        };

        var handle = GCHandle.Alloc(context);

        var nameBlock = AllocateUtf8(DescribeTarget(target));
        var methodDefBlock = Marshal.AllocHGlobal(sizeof(PyMethodDef));

        var methodDef = (PyMethodDef*)methodDefBlock;
        methodDef->Name = nameBlock;
        methodDef->Method = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr>)&Trampoline;
        methodDef->Flags = METH_VARARGS | METH_KEYWORDS;
        methodDef->Doc = IntPtr.Zero;

        context.MethodDef = methodDefBlock;
        context.NameBlock = nameBlock;

        var capsule = NativeMethods.PyCapsule_NewRaw(
            GCHandle.ToIntPtr(handle),
            _capsuleNamePin.AddrOfPinnedObject(),
            (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, void>)&CapsuleDestructor);

        if (capsule == IntPtr.Zero)
        {
            Release(handle, methodDefBlock, nameBlock);
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException("PyCapsule_New returned null while creating a callback.");
        }

        // The function takes its own reference to the capsule, so this one is handed over.
        var function = NativeMethods.PyCFunction_NewEx(methodDefBlock, capsule, IntPtr.Zero);
        NativeMethods.Py_DecRef(capsule);

        if (function == IntPtr.Zero)
        {
            // Dropping the capsule above already ran the destructor, which released
            // everything; nothing further to clean up here.
            PythonException.ThrowIfPythonErrorOccurred();
            throw new PyInteropException("PyCFunction_NewEx returned null while creating a callback.");
        }

        return function;
    }

    /// <summary>
    /// Invoked by CPython for every call. Runs with the GIL held and must never let a
    /// managed exception escape into native code — returning null with a Python error set
    /// is the only way to report failure across this boundary.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static IntPtr Trampoline(IntPtr self, IntPtr args, IntPtr kwargs)
    {
        try
        {
            var pointer = NativeMethods.PyCapsule_GetPointerRaw(
                self, _capsuleNamePin.AddrOfPinnedObject());

            if (pointer == IntPtr.Zero)
            {
                NativeMethods.PyErr_Clear();
                SetError("RuntimeError", "PyDotNet callback is no longer valid.");
                return IntPtr.Zero;
            }

            if (GCHandle.FromIntPtr(pointer).Target is not CallbackContext context)
            {
                SetError("RuntimeError", "PyDotNet callback has already been released.");
                return IntPtr.Zero;
            }

            if (!TryBindArguments(context, args, kwargs, out var values))
            {
                return IntPtr.Zero;
            }

            // Calls the delegate directly rather than through DynamicInvoke, so whatever it
            // throws arrives here unwrapped and the catch below reports the real failure
            // rather than a TargetInvocationException that says nothing.
            var result = context.Invoke(context.Target, values);

            return context.ReturnsVoid ? TypeConverter.GetNone() : TypeConverter.ToPython(result);
        }
        catch (Exception ex)
        {
            SetErrorFrom(ex);
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// Maps the Python call's positional and keyword arguments onto the delegate's
    /// parameters, following Python's own rules. Sets a Python error and returns
    /// <see langword="false"/> if they cannot be satisfied.
    /// </summary>
    private static bool TryBindArguments(
        CallbackContext context,
        IntPtr args,
        IntPtr kwargs,
        out object?[] values)
    {
        var parameters = context.Parameters;
        values = parameters.Length == 0 ? [] : new object?[parameters.Length];

        var positional = args == IntPtr.Zero ? 0 : (int)NativeMethods.PyTuple_Size(args);
        if (positional < 0)
        {
            NativeMethods.PyErr_Clear();
            positional = 0;
        }

        if (positional > parameters.Length)
        {
            SetError(
                "TypeError",
                $"{DescribeTarget(context.Target)}() takes {parameters.Length} " +
                $"argument{(parameters.Length == 1 ? string.Empty : "s")} but {positional} were given");
            return false;
        }

        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            IntPtr value;

            if (i < positional)
            {
                value = NativeMethods.PyTuple_GetItem(args, i); // borrowed
            }
            else
            {
                value = kwargs == IntPtr.Zero || parameter.Name is null
                    ? IntPtr.Zero
                    : NativeMethods.PyDict_GetItemString(kwargs, parameter.Name); // borrowed
            }

            if (value == IntPtr.Zero)
            {
                NativeMethods.PyErr_Clear();

                if (parameter.HasDefaultValue)
                {
                    values[i] = parameter.DefaultValue;
                    continue;
                }

                SetError(
                    "TypeError",
                    $"{DescribeTarget(context.Target)}() missing required argument '{parameter.Name}'");
                return false;
            }

            try
            {
                values[i] = TypeConverter.FromPython(value, parameter.ParameterType);
            }
            catch (Exception ex)
            {
                // Named rather than left as a bare conversion failure: which argument could
                // not be converted is the whole of the diagnosis.
                SetError(
                    "TypeError",
                    $"{DescribeTarget(context.Target)}() could not convert argument " +
                    $"'{parameter.Name}' to {parameter.ParameterType.Name}: {ex.Message}");
                return false;
            }
        }

        return kwargs == IntPtr.Zero || TryRejectUnknownKeywords(context, kwargs);
    }

    /// <summary>
    /// Reports keyword arguments the delegate has no parameter for, the way Python does.
    /// Accepting them silently would discard the caller's intent — a misspelled
    /// <c>reverse=</c> would simply not happen.
    /// </summary>
    private static bool TryRejectUnknownKeywords(CallbackContext context, IntPtr kwargs)
    {
        var keys = NativeMethods.PyDict_Keys(kwargs);
        if (keys == IntPtr.Zero)
        {
            NativeMethods.PyErr_Clear();
            return true;
        }

        try
        {
            var count = NativeMethods.PyList_Size(keys);
            for (nint i = 0; i < count; i++)
            {
                var key = NativeMethods.PyList_GetItem(keys, i); // borrowed
                if (key == IntPtr.Zero)
                {
                    NativeMethods.PyErr_Clear();
                    continue;
                }

                var text = NativeMethods.PyUnicode_AsUTF8(key);
                if (text == IntPtr.Zero)
                {
                    NativeMethods.PyErr_Clear();
                    continue;
                }

                var name = Marshal.PtrToStringUTF8(text);
                if (name is null)
                {
                    continue;
                }

                var known = false;
                foreach (var parameter in context.Parameters)
                {
                    if (string.Equals(parameter.Name, name, StringComparison.Ordinal))
                    {
                        known = true;
                        break;
                    }
                }

                if (!known)
                {
                    SetError(
                        "TypeError",
                        $"{DescribeTarget(context.Target)}() got an unexpected keyword argument '{name}'");
                    return false;
                }
            }
        }
        finally
        {
            NativeMethods.Py_DecRef(keys);
        }

        return true;
    }

    /// <summary>
    /// Runs when Python collects the callable. Releases the delegate and the unmanaged
    /// blocks the method definition points at.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CapsuleDestructor(IntPtr capsule)
    {
        try
        {
            var pointer = NativeMethods.PyCapsule_GetPointerRaw(
                capsule, _capsuleNamePin.AddrOfPinnedObject());

            if (pointer == IntPtr.Zero)
            {
                NativeMethods.PyErr_Clear();
                return;
            }

            var handle = GCHandle.FromIntPtr(pointer);
            var context = handle.Target as CallbackContext;

            Release(handle, context?.MethodDef ?? IntPtr.Zero, context?.NameBlock ?? IntPtr.Zero);
        }
        catch
        {
            // A destructor cannot report anything: it runs during deallocation, where
            // raising would corrupt whatever CPython is in the middle of. Leaking one
            // callback's blocks is the lesser outcome.
        }
    }

    private static void Release(GCHandle handle, IntPtr methodDef, IntPtr nameBlock)
    {
        if (handle.IsAllocated)
        {
            handle.Free();
        }

        // Freed after the handle, so nothing can still reach them through the context.
        if (methodDef != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(methodDef);
        }

        if (nameBlock != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(nameBlock);
        }
    }

    /// <summary>
    /// Translates a .NET exception into a Python one, so a failure inside the callback
    /// looks to Python like any other error rather than a silently swallowed call.
    /// </summary>
    private static void SetErrorFrom(Exception exception)
    {
        // An exception that started in Python is raised again as the type it was, so a
        // Python → .NET → Python round trip does not degrade it to a generic error.
        if (exception is PythonException pythonException)
        {
            SetError(pythonException.PythonExceptionType, pythonException.Message);
            return;
        }

        // The .NET type name is kept in the message: the mapping below is deliberately
        // coarse, and discarding what actually went wrong to fit a Python type would be a
        // poor trade.
        SetError(MapExceptionType(exception), $"{exception.GetType().Name}: {exception.Message}");
    }

    private static string MapExceptionType(Exception exception) => exception switch
    {
        // Ordered so a derived type is matched before the base it would otherwise fall
        // into: ArgumentOutOfRangeException is an ArgumentException.
        ArgumentOutOfRangeException or IndexOutOfRangeException => "IndexError",
        KeyNotFoundException => "KeyError",
        ArgumentException => "ValueError",
        InvalidCastException => "TypeError",
        FormatException => "ValueError",
        NotImplementedException or NotSupportedException => "NotImplementedError",
        OverflowException => "OverflowError",
        DivideByZeroException => "ZeroDivisionError",
        TimeoutException => "TimeoutError",
        OutOfMemoryException => "MemoryError",
        IOException => "OSError",
        _ => "RuntimeError",
    };

    /// <summary>Sets a Python exception of the named builtin type.</summary>
    private static void SetError(string typeName, string message)
    {
        var type = ResolveBuiltinException(typeName)
            ?? ResolveBuiltinException("RuntimeError");

        if (type is null)
        {
            // builtins is unreachable, which means the interpreter is in no state to be
            // told anything. Leave whatever error is already set.
            return;
        }

        NativeMethods.PyErr_SetString(type.Value, message);
    }

    /// <summary>
    /// Resolves a builtin exception type by name, or <see langword="null"/> when there is
    /// no such builtin — a Python exception defined in user code, for instance, whose type
    /// object is not reachable from here.
    /// </summary>
    private static IntPtr? ResolveBuiltinException(string typeName)
    {
        if (_builtinExceptions.TryGetValue(typeName, out var cached))
        {
            return cached == IntPtr.Zero ? null : cached;
        }

        var resolved = IntPtr.Zero;

        var builtins = NativeMethods.PyImport_ImportModule("builtins");
        if (builtins == IntPtr.Zero)
        {
            NativeMethods.PyErr_Clear();
        }
        else
        {
            try
            {
                var type = NativeMethods.PyObject_GetAttrString(builtins, typeName);
                if (type == IntPtr.Zero)
                {
                    NativeMethods.PyErr_Clear();
                }
                else
                {
                    // Builtin exception types live for the life of the interpreter, so this
                    // reference is deliberately never released — the alternative is a
                    // lookup on every failed call.
                    resolved = type;
                }
            }
            finally
            {
                NativeMethods.Py_DecRef(builtins);
            }
        }

        // Negative results are cached too, so a user-defined type name is not looked up
        // again on every raise.
        _builtinExceptions[typeName] = resolved;
        return resolved == IntPtr.Zero ? null : resolved;
    }

    /// <summary>
    /// Returns the invoker for a delegate type, compiling it on first use.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The alternative, <c>Delegate.DynamicInvoke</c>, walks parameter metadata and boxes
    /// through reflection on every call — and this is a per-element path, not a per-call
    /// one: <c>sorted(key=...)</c> invokes the callback once per comparison, and
    /// <c>DataFrame.apply</c> once per row. The marshaling cost per argument is inherent to
    /// the boundary; the reflection on top of it is not.
    /// </para>
    /// <para>
    /// Keyed by delegate <em>type</em> rather than by delegate instance, so the compilation
    /// is paid once per signature and shared by every callable of that shape. Keying by
    /// instance would move the cost onto <see cref="Create"/>, which callers may run in a
    /// loop — and the set of distinct signatures in a program is small, while the set of
    /// closures need not be.
    /// </para>
    /// <para>
    /// Under NativeAOT this falls back to the expression interpreter, which is no worse
    /// than the reflection it replaces. Emitting these from a source generator is the
    /// AOT-correct answer and the reason the invoke path is a single seam.
    /// </para>
    /// </remarks>
    private static Func<Delegate, object?[], object?> GetInvoker(
        Type delegateType,
        ParameterInfo[] parameters,
        Type returnType)
    {
        return _invokers.GetOrAdd(
            delegateType,
            static (_, state) => BuildInvoker(state.Type, state.Parameters, state.ReturnType),
            (Type: delegateType, Parameters: parameters, ReturnType: returnType));
    }

    private static Func<Delegate, object?[], object?> BuildInvoker(
        Type delegateType,
        ParameterInfo[] parameters,
        Type returnType)
    {
        var targetParameter = Expression.Parameter(typeof(Delegate), "target");
        var argumentsParameter = Expression.Parameter(typeof(object?[]), "args");

        // The trampoline hands back the same delegate the context holds, so this cast
        // always succeeds; it is what lets one compiled invoker serve every instance.
        var typedTarget = Expression.Convert(targetParameter, delegateType);

        var arguments = new Expression[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            arguments[i] = Expression.Convert(
                Expression.ArrayIndex(argumentsParameter, Expression.Constant(i)),
                parameters[i].ParameterType);
        }

        Expression body = Expression.Invoke(typedTarget, arguments);

        // A void delegate has no value to hand back, so the lambda yields null rather than
        // the caller having to special-case the return type at every call.
        body = returnType == typeof(void)
            ? Expression.Block(body, Expression.Constant(null, typeof(object)))
            : Expression.Convert(body, typeof(object));

        return Expression.Lambda<Func<Delegate, object?[], object?>>(
            body, targetParameter, argumentsParameter).Compile();
    }

    /// <summary>
    /// Copies <paramref name="value"/> into an unmanaged NUL-terminated UTF-8 block.
    /// CPython reads <c>ml_name</c> as <c>const char*</c> and keeps the pointer, so the
    /// block has to outlive the callable rather than a marshalling scope.
    /// </summary>
    private static IntPtr AllocateUtf8(string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        var block = Marshal.AllocHGlobal(bytes.Length + 1);

        Marshal.Copy(bytes, 0, block, bytes.Length);
        Marshal.WriteByte(block, bytes.Length, 0);

        return block;
    }

    private static bool IsAwaitable(Type type)
    {
        if (type == typeof(Task) || type == typeof(ValueTask))
        {
            return true;
        }

        if (!type.IsGenericType)
        {
            return false;
        }

        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(Task<>) || definition == typeof(ValueTask<>);
    }

    /// <summary>
    /// A name for the callable, used for <c>__name__</c> and in error messages. Lambdas get
    /// compiler-generated names such as <c>&lt;Main&gt;b__0_1</c>, which are unhelpful, so
    /// those fall back to something that at least says what it is.
    /// </summary>
    private static string DescribeTarget(Delegate target)
    {
        var name = target.Method.Name;

        return name.Contains('<', StringComparison.Ordinal) ? "pydotnet_callback" : name;
    }
}
