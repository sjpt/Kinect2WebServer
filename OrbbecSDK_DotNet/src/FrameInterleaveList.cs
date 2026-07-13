using System;
using System.Runtime.InteropServices;

namespace Orbbec
{
    public class FrameInterleaveList : IDisposable
    {
        private NativeHandle _handle;

        internal FrameInterleaveList(IntPtr handle)
        {
            _handle = new NativeHandle(handle, Delete);
        }

        /**
        * @brief Get the number of frame interleave in the list
        *
        * @return The number of frame interleave in the list
        */
        public UInt32 Count()
        {
            IntPtr error = IntPtr.Zero;
            UInt32 count = obNative.ob_device_frame_interleave_list_get_count(_handle.Ptr, ref error);
            NativeException.HandleError(error);
            return count;
        }

        /**
        * @brief Get the name of the frame interleave at the specified index
        *
        * @param index The index of the frame interleave
        * @return The name of the frame interleave
        */
        public String GetName(uint index)
        {
            IntPtr error = IntPtr.Zero;
            IntPtr ptr = obNative.ob_device_frame_interleave_list_get_name(_handle.Ptr, index, ref error);
            NativeException.HandleError(error);
            return Marshal.PtrToStringAnsi(ptr);
        }

        /**
        * @brief Check if the frame interleave list contains the specified name
        *
        * @param name The name of the frame interleave
        * @return Returns true if the name is found in the list
        */
        public bool HasFrameInterleave(String name)
        {
            IntPtr error = IntPtr.Zero;
            bool result = obNative.ob_device_frame_interleave_list_has_frame_interleave(_handle.Ptr, name, ref error);
            NativeException.HandleError(error);
            return result;
        }

        internal void Delete(IntPtr handle)
        {
            IntPtr error = IntPtr.Zero;
            obNative.ob_delete_frame_interleave_list(handle, ref error);
            NativeException.HandleError(error);
        }

        public void Dispose()
        {
            _handle.Dispose();
        }
    }
}
