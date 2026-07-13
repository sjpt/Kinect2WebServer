using System;

namespace Orbbec
{
    public class PresetResolutionConfigList : IDisposable
    {
        private NativeHandle _handle;

        internal PresetResolutionConfigList(IntPtr handle)
        {
            _handle = new NativeHandle(handle, Delete);
        }

        /**
        * \if English
        * @brief Get the number of preset-resolution configuration items supported by the device.
        *
        * @return UInt32  Number of preset-resolution configurations.
        * \else
        * @brief 获取设备支持的预设分辨率配置数量
        *
        * @return UInt32  预设分辨率配置数量
        * \endif
        */
        public UInt32 Count()
        {
            IntPtr error = IntPtr.Zero;
            UInt32 count = obNative.ob_device_preset_resolution_config_get_count(_handle.Ptr, ref error);
            NativeException.HandleError(error);
            return count;
        }

        /**
        * \if English
        * @brief Get a preset-resolution configuration item from the device.
        *
        * @param index   Index of the preset-resolution configuration.
        * @return OBPresetResolutionConfig  The preset-resolution configuration at the given index.
        * \else
        * @brief 获取指定索引的预设分辨率配置
        *
        * @param index   配置索引
        * @return OBPresetResolutionConfig  对应的预设分辨率配置
        * \endif
        */
        public OBPresetResolutionConfig GetPresetResolutionRatioConfig(UInt32 index)
        {
            IntPtr error = IntPtr.Zero;
            OBPresetResolutionConfig presetResolutionConfig = obNative.ob_device_preset_resolution_config_list_get_item(_handle.Ptr, index, ref error);
            NativeException.HandleError(error);
            return presetResolutionConfig;
        }

        internal void Delete(IntPtr handle)
        {
            IntPtr error = IntPtr.Zero;
            obNative.ob_delete_preset_resolution_config_list(handle, ref error);
            NativeException.HandleError(error);
        }

        public void Dispose()
        {
            _handle.Dispose();
        }
    }
}