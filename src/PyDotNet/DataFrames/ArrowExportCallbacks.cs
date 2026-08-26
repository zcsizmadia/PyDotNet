using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using PyDotNet.Native;

namespace PyDotNet.DataFrames;

/// <summary>
/// The unmanaged entry points Arrow consumers call back into.
/// </summary>
/// <remarks>
/// Separate from <see cref="ArrowExport"/> so that <see cref="ExportState"/> can take their
/// addresses while building the structs — a function pointer to an
/// <see cref="UnmanagedCallersOnlyAttribute"/> method can only be taken where the method is
/// visible.
/// <para>
/// None of these may let a managed exception escape: they are invoked from C, where an
/// unwinding .NET exception is undefined behaviour. Each returns an error code or, for the
/// release callbacks, swallows — a release has nobody to report to.
/// </para>
/// </remarks>
internal static unsafe class ArrowExportCallbacks
{
    /// <summary><c>EINVAL</c>, the errno Arrow expects for a bad request.</summary>
    private const int Einval = 22;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    internal static int GetSchemaThunk(ArrowCDataInterface* stream, ArrowSchema* output)
    {
        try
        {
            if (stream is null || output is null || StateOf(stream->PrivateData) is not { } state)
            {
                return Einval;
            }

            state.FillSchema(output);
            return 0;
        }
        catch
        {
            return Einval;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    internal static int GetNextThunk(ArrowCDataInterface* stream, ArrowArray* output)
    {
        try
        {
            if (stream is null || output is null || StateOf(stream->PrivateData) is not { } state)
            {
                return Einval;
            }

            if (state.Delivered)
            {
                // End of stream is signalled by a released array — a zeroed struct, whose
                // null release is what the consumer tests.
                *output = default;
                return 0;
            }

            state.Delivered = true;
            state.FillArray(output);
            return 0;
        }
        catch
        {
            return Einval;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    internal static byte* GetLastErrorThunk(ArrowCDataInterface* stream) => null;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    internal static void ReleaseStreamThunk(ArrowCDataInterface* stream)
    {
        if (stream is null || stream->Release is null)
        {
            return;
        }

        try
        {
            var handle = GCHandle.FromIntPtr((IntPtr)stream->PrivateData);
            if (handle.Target is ExportState state)
            {
                // Only the stream's own scaffolding. The batch's buffers belong to the
                // exported array and are freed by its release callback, which may not have
                // run yet: a consumer may release the stream and keep the batches it took.
                state.ReleaseStreamSide();
            }

            handle.Free();
        }
        catch
        {
        }

        stream->Release = null;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    internal static void ReleaseSchemaThunk(ArrowSchema* schema)
    {
        if (schema is null || schema->Release is null)
        {
            return;
        }

        try
        {
            // Children carry no private data: the parent's bag owns their memory, so
            // releasing one is just marking it released.
            if (schema->PrivateData is not null)
            {
                var handle = GCHandle.FromIntPtr((IntPtr)schema->PrivateData);
                (handle.Target as BlockBag)?.Free();
                handle.Free();
            }
        }
        catch
        {
        }

        schema->Release = null;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    internal static void ReleaseArrayThunk(ArrowArray* array)
    {
        if (array is null || array->Release is null)
        {
            return;
        }

        try
        {
            if (array->PrivateData is not null)
            {
                var handle = GCHandle.FromIntPtr((IntPtr)array->PrivateData);
                (handle.Target as BlockBag)?.Free();
                handle.Free();
            }
        }
        catch
        {
        }

        array->Release = null;
    }

    /// <summary>
    /// Frees a stream that Python collected without any consumer taking it, so the pins do
    /// not outlive the object that owned them.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    internal static void CapsuleDestructorThunk(IntPtr capsule)
    {
        try
        {
            var namePtr = ArrowExport.CapsuleNamePointer;

            // An importer that consumed the capsule renames it, at which point the stream
            // belongs to them and releasing it here would be a double free.
            if (NativeMethods.PyCapsule_IsValidRaw(capsule, namePtr) != 1)
            {
                NativeMethods.PyErr_Clear();
                return;
            }

            var pointer = NativeMethods.PyCapsule_GetPointerRaw(capsule, namePtr);
            if (pointer == IntPtr.Zero)
            {
                NativeMethods.PyErr_Clear();
                return;
            }

            var stream = (ArrowCDataInterface*)pointer;
            if (stream->Release is not null)
            {
                stream->Release(stream);
            }

            NativeMemory.Free(stream);
        }
        catch
        {
        }
    }

    private static ExportState? StateOf(void* privateData)
    {
        return privateData is null
            ? null
            : GCHandle.FromIntPtr((IntPtr)privateData).Target as ExportState;
    }
}
