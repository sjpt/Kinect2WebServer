using System;
using System.Runtime.InteropServices;

namespace Orbbec
{    
    public class StreamProfile : IDisposable
    {
        protected NativeHandle _handle;

        internal StreamProfile(IntPtr handle)
        {
            _handle = new NativeHandle(handle, Delete);
        }

        internal StreamProfile(NativeHandle handle)
        {
            _handle = handle;
            _handle.Retain();
        }

        internal NativeHandle GetNativeHandle()
        {
            return _handle;
        }

        public T As<T>() where T : StreamProfile {
            switch (GetStreamType())
            {
                case StreamType.OB_STREAM_VIDEO:
                case StreamType.OB_STREAM_IR:
                case StreamType.OB_STREAM_IR_LEFT:
                case StreamType.OB_STREAM_IR_RIGHT:
                case StreamType.OB_STREAM_COLOR:
                case StreamType.OB_STREAM_COLOR_LEFT:
                case StreamType.OB_STREAM_COLOR_RIGHT:
                case StreamType.OB_STREAM_DEPTH:
                case StreamType.OB_STREAM_CONFIDENCE:
                    return new VideoStreamProfile(_handle) as T;
                case StreamType.OB_STREAM_ACCEL:
                    return new AccelStreamProfile(_handle) as T;
                case StreamType.OB_STREAM_GYRO:
                    return new GyroStreamProfile(_handle) as T;
                case StreamType.OB_STREAM_LIDAR:
                    return new LiDARStreamProfile(_handle) as T;
            }
            return null;
        }

        public static StreamProfile Create(StreamType streamType, Format format)
        {
            IntPtr error = IntPtr.Zero;
            IntPtr handle = obNative.ob_create_stream_profile(streamType, format, ref error);
            NativeException.HandleError(error);
            return new StreamProfile(handle);
        }

        public static StreamProfile CreateFromOtherStreamProfile(StreamProfile srcProfile)
        {
            IntPtr error = IntPtr.Zero;
            IntPtr handle = obNative.ob_create_stream_profile_from_other_stream_profile(srcProfile.GetNativeHandle().Ptr, ref error);
            NativeException.HandleError(error);
            return new StreamProfile(handle);
        }

        public static StreamProfile CreateWithNewFormat(StreamProfile profile, Format format)
        {
            IntPtr error = IntPtr.Zero;
            IntPtr handle = obNative.ob_create_stream_profile_with_new_format(profile.GetNativeHandle().Ptr, format, ref error);
            NativeException.HandleError(error);
            return new StreamProfile(handle);
        }

        /**
        * \if English
        * @brief Get the format of the stream
        *
        * @return Format returns the format of the stream
        * \else
        * @brief 获取流的格式
        *
        * @return Format 返回流的格式
        * \endif
        */
        public Format GetFormat()
        {
            IntPtr error = IntPtr.Zero;
            Format format = obNative.ob_stream_profile_get_format(_handle.Ptr, ref error);
            NativeException.HandleError(error);
            return format;
        }

        public void SetFormat(Format format)
        {
            IntPtr error = IntPtr.Zero;
            obNative.ob_stream_profile_set_format(_handle.Ptr, format, ref error);
            NativeException.HandleError(error);
        }

        public void SetStreamType(StreamType streamType)
        {
            IntPtr error = IntPtr.Zero;
            obNative.ob_stream_profile_set_type(_handle.Ptr, streamType, ref error);
            NativeException.HandleError(error);
        }

        public void SetExtrinsicTo(StreamProfile targetProfile, Extrinsic extrinsic)
        {
            IntPtr error = IntPtr.Zero;
            obNative.ob_stream_profile_set_extrinsic_to(_handle.Ptr, targetProfile.GetNativeHandle().Ptr, extrinsic, ref error);
            NativeException.HandleError(error);
        }

        /**
        * \if English
        * @brief Set the extrinsic parameters from current stream profile to the given target stream type.
        *
        * @param targetStreamType Target stream type.
        * @param extrinsic The extrinsic parameters.
        * \else
        * @brief 设置从当前流配置文件到给定目标流类型的外参。
        *
        * @param targetStreamType 目标流类型。
        * @param extrinsic 外参。
        * \endif
        */
        public void BindExtrinsicTo(StreamType targetStreamType, Extrinsic extrinsic)
        {
            IntPtr error = IntPtr.Zero;
            obNative.ob_stream_profile_set_extrinsic_to_type(_handle.Ptr, targetStreamType, extrinsic, ref error);
            NativeException.HandleError(error);
        }

        /**
        * \if English
        * @brief Get the type of stream
        *
        * @return StreamType returns the type of the stream
        * \else
        * @brief 获取流的类型
        *
        * @return StreamType 返回流的类型
        * \endif
        */
        public StreamType GetStreamType()
        {
            IntPtr error = IntPtr.Zero;
            StreamType streamType = obNative.ob_stream_profile_get_type(_handle.Ptr, ref error);
            NativeException.HandleError(error);
            return streamType;
        }

        public DisparityParam DisparityBasedStreamProfileGetDisparityParam()
        {
            IntPtr error = IntPtr.Zero;
            DisparityParam param;
            obNative.ob_disparity_based_stream_profile_get_disparity_param(out param, _handle.Ptr, ref error);
            NativeException.HandleError(error);
            return param;
        }

        public void DisparityBasedStreamProfileSetDisparityParam(DisparityParam param)
        {
            IntPtr error = IntPtr.Zero;
            obNative.ob_disparity_based_stream_profile_set_disparity_param(_handle.Ptr, param, ref error);
            NativeException.HandleError(error);
        }

        public Extrinsic GetExtrinsicTo(StreamProfile target)
        {
            IntPtr error = IntPtr.Zero;
            Extrinsic extrinsic = obNative.ob_stream_profile_get_extrinsic_to(_handle.Ptr, target.GetNativeHandle().Ptr, ref error);
            NativeException.HandleError(error);
            return extrinsic;
        }

        internal void Delete(IntPtr handle)
        {
            IntPtr error = IntPtr.Zero;
            obNative.ob_delete_stream_profile(handle, ref error);
            NativeException.HandleError(error);
        }

        public void Dispose()
        {
            _handle.Dispose();
        }
    }

    public class VideoStreamProfile : StreamProfile
    {
        internal VideoStreamProfile(IntPtr handle) : base(handle)
        {   
        }

        internal VideoStreamProfile(NativeHandle handle) : base(handle)
        {
        }

        public static VideoStreamProfile Create(StreamType streamType, Format format, UInt32 width, UInt32 height, UInt32 fps)
        {
            IntPtr error = IntPtr.Zero;
            IntPtr handle = obNative.ob_create_video_stream_profile(streamType, format, width, height, fps, ref error);
            NativeException.HandleError(error);
            return new VideoStreamProfile(handle);
        }

        /**
        * \if English
        * @brief Get stream frame rate
        *
        * @return UInt32 returns the frame rate of the stream
        * \else
        * @brief 获取流的帧率
        *
        * @return UInt32 返回流的帧率
        * \endif
        */
        public UInt32 GetFPS()
        {
            IntPtr error = IntPtr.Zero;
            UInt32 fps = obNative.ob_video_stream_profile_get_fps(_handle.Ptr, ref error);
            NativeException.HandleError(error);
            return fps;
        }

        /**
        * \if English
        * @brief Get stream width
        *
        * @return UInt32 returns the width of the stream
        * \else
        * @brief 获取流的宽
        *
        * @return UInt32 返回流的宽
        * \endif
        */
        public UInt32 GetWidth()
        {
            IntPtr error = IntPtr.Zero;
            UInt32 width = obNative.ob_video_stream_profile_get_width(_handle.Ptr, ref error);
            NativeException.HandleError(error);
            return width;
        }

        /**
        * \if English
        * @brief Get stream height
        *
        * @return UInt32 returns the high of the stream
        * \else
        * @brief 获取流的高
        *
        * @return UInt32 返回流的高
        * \endif
        */
        public UInt32 GetHeight()
        {
            IntPtr error = IntPtr.Zero;
            UInt32 height = obNative.ob_video_stream_profile_get_height(_handle.Ptr, ref error);
            NativeException.HandleError(error);
            return height;
        }

        public void SetWidth(UInt32 width)
        {
            IntPtr error = IntPtr.Zero;
            obNative.ob_video_stream_profile_set_width(_handle.Ptr, width, ref error);
            NativeException.HandleError(error);
        }

        public void SetHeight(UInt32 height)
        {
            IntPtr error = IntPtr.Zero;
            obNative.ob_video_stream_profile_set_height(_handle.Ptr, height, ref error);
            NativeException.HandleError(error);
        }

        public void SetIntrinsic(CameraIntrinsic intrinsic)
        {
            IntPtr error = IntPtr.Zero;
            obNative.ob_video_stream_profile_set_intrinsic(_handle.Ptr, intrinsic, ref error);
            NativeException.HandleError(error);
        }

        public CameraIntrinsic GetIntrinsic()
        {
            IntPtr error = IntPtr.Zero;
            CameraIntrinsic intrinsic = obNative.ob_video_stream_profile_get_intrinsic(_handle.Ptr, ref error);
            NativeException.HandleError(error);
            return intrinsic;
        }

        public void SetCameraDistortion(CameraDistortion distortion)
        {
            IntPtr error = IntPtr.Zero;
            obNative.ob_video_stream_profile_set_distortion(_handle.Ptr, distortion, ref error);
            NativeException.HandleError(error);
        }

        public CameraDistortion GetDistortion()
        {
            IntPtr error = IntPtr.Zero;
            CameraDistortion distortion = obNative.ob_video_stream_profile_get_distortion(_handle.Ptr, ref error);
            NativeException.HandleError(error);
            return distortion;
        }

        /**
        * \if English
        * @brief Get the decimation configuration of the stream.
        *        Includes original resolution and scale factor.
        *
        * @return HardwareDecimationConfig returns the decimation configuration.
        * \else
        * @brief 获取流的抽取配置。
        *        包括原始分辨率和缩放因子。
        *
        * @return HardwareDecimationConfig 返回抽取配置。
        * \endif
        */
        public HardwareDecimationConfig GetDecimationConfig()
        {
            IntPtr error = IntPtr.Zero;
            HardwareDecimationConfig config = obNative.ob_video_stream_profile_get_decimation_config(_handle.Ptr, ref error);
            NativeException.HandleError(error);
            return config;
        }
    }

    public class AccelStreamProfile : StreamProfile
    {
        internal AccelStreamProfile(IntPtr handle) : base(handle)
        {
        }

        internal AccelStreamProfile(NativeHandle handle) : base(handle)
        {
        }

        public static AccelStreamProfile Create(AccelFullScaleRange fullScaleRange, AccelSampleRate sampleRate)
        {
            IntPtr error = IntPtr.Zero;
            IntPtr handle = obNative.ob_create_accel_stream_profile(fullScaleRange, sampleRate, ref error);
            NativeException.HandleError(error);
            return new AccelStreamProfile(handle);
        }

        /**
        * \if English
        * @brief Get full scale range
        *
        * @return AccelFullScaleRange  returns the scale range value
        * \else
        * @brief 获取满量程范围
        *
        * @return AccelFullScaleRange  返回量程范围值
        * \endif
        */
        public AccelFullScaleRange GetFullScaleRange()
        {
            IntPtr error = IntPtr.Zero;
            AccelFullScaleRange accelFullScaleRange = obNative.ob_accel_stream_profile_get_full_scale_range(_handle.Ptr, ref error);
            NativeException.HandleError(error);
            return accelFullScaleRange;
        } 

        /**
        * \if English
        * @brief Get sampling frequency
        *
        * @return AccelSampleRate  returns the sampling frequency
        * \else
        * @brief 获取采样频率
        *
        * @return AccelSampleRate  返回采样频率
        * \endif
        */
        public AccelSampleRate GetSampleRate()
        {
            IntPtr error = IntPtr.Zero;
            AccelSampleRate accelSampleRate = obNative.ob_accel_stream_profile_get_sample_rate(_handle.Ptr, ref error);
            NativeException.HandleError(error);
            return accelSampleRate;
        }

        public void SetIntrinsic(AccelIntrinsic intrinsic)
        {
            IntPtr error = IntPtr.Zero;
            obNative.ob_accel_stream_profile_set_intrinsic(_handle.Ptr, intrinsic, ref error);
            NativeException.HandleError(error);
        }
    }

    public class GyroStreamProfile : StreamProfile
    {
        internal GyroStreamProfile(IntPtr handle) : base(handle)
        {
        }

        internal GyroStreamProfile(NativeHandle handle) : base(handle)
        {
        }

        public static GyroStreamProfile Create(GyroFullScaleRange fullScaleRange, GyroSampleRate sampleRate)
        {
            IntPtr error = IntPtr.Zero;
            IntPtr handle = obNative.ob_create_gyro_stream_profile(fullScaleRange, sampleRate, ref error);
            NativeException.HandleError(error);
            return new GyroStreamProfile(handle);
        }

        /**
        * \if English
        * @brief Get full scale range
        *
        * @return GyroFullScaleRange  returns the scale range value
        * \else
        * @brief 获取满量程范围
        *
        * @return GyroFullScaleRange  返回量程范围值
        * \endif
        */
        public GyroFullScaleRange GetFullScaleRange()
        {
            IntPtr error = IntPtr.Zero;
            GyroFullScaleRange gyroFullScaleRange = obNative.ob_gyro_stream_profile_get_full_scale_range(_handle.Ptr, ref error);
            NativeException.HandleError(error);
            return gyroFullScaleRange;
        } 

        /**
        * \if English
        * @brief Get sampling frequency
        *
        * @return GyroSampleRate  returns the sampling frequency
        * \else
        * @brief 获取采样频率
        *
        * @return GyroSampleRate  返回采样频率
        * \endif
        */
        public GyroSampleRate GetSampleRate()
        {
            IntPtr error = IntPtr.Zero;
            GyroSampleRate gyroSampleRate = obNative.ob_gyro_stream_profile_get_sample_rate(_handle.Ptr, ref error);
            NativeException.HandleError(error);
            return gyroSampleRate;
        }

        public void SetIntrinsic(GyroIntrinsic intrinsic)
        {
            IntPtr error = IntPtr.Zero;
            obNative.ob_gyro_stream_set_intrinsic(_handle.Ptr, intrinsic, ref error);
            NativeException.HandleError(error);
        }
    }

    /**
    * \if English
    * @brief LiDAR stream profile class
    * \else
    * @brief LiDAR流配置文件类
    * \endif
    */
    public class LiDARStreamProfile : StreamProfile
    {
        internal LiDARStreamProfile(IntPtr handle) : base(handle)
        {
        }

        internal LiDARStreamProfile(NativeHandle handle) : base(handle)
        {
        }

        public static LiDARStreamProfile Create(LiDARScanRate scanRate, Format format)
        {
            IntPtr error = IntPtr.Zero;
            IntPtr handle = obNative.ob_create_lidar_stream_profile(scanRate, format, ref error);
            NativeException.HandleError(error);
            return new LiDARStreamProfile(handle);
        }

        /**
        * \if English
        * @brief Get LiDAR scan rate
        *
        * @return LiDARScanRate returns the scan rate
        * \else
        * @brief 获取LiDAR扫描速率
        *
        * @return LiDARScanRate 返回扫描速率
        * \endif
        */
        public LiDARScanRate GetScanRate()
        {
            IntPtr error = IntPtr.Zero;
            LiDARScanRate rate = obNative.ob_lidar_stream_profile_get_scan_rate(_handle.Ptr, ref error);
            NativeException.HandleError(error);
            return rate;
        }
    }

    /**
    * \if English
    * @brief Stream profile factory class for creating stream profile objects
    * \else
    * @brief 流配置文件工厂类，用于创建流配置文件对象
    * \endif
    */
    public static class StreamProfileFactory
    {
        /**
        * \if English
        * @brief Create a stream profile from native handle based on stream type
        * \else
        * @brief 根据流类型从原生句柄创建流配置文件
        * \endif
        */
        public static StreamProfile Create(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            // Get stream type to determine which class to instantiate
            IntPtr error = IntPtr.Zero;
            StreamType type = obNative.ob_stream_profile_get_type(handle, ref error);
            NativeException.HandleError(error);

            switch (type)
            {
                case StreamType.OB_STREAM_IR:
                case StreamType.OB_STREAM_IR_LEFT:
                case StreamType.OB_STREAM_IR_RIGHT:
                case StreamType.OB_STREAM_DEPTH:
                case StreamType.OB_STREAM_COLOR:
                case StreamType.OB_STREAM_COLOR_LEFT:
                case StreamType.OB_STREAM_COLOR_RIGHT:
                case StreamType.OB_STREAM_CONFIDENCE:
                    return new VideoStreamProfile(handle);
                case StreamType.OB_STREAM_ACCEL:
                    return new AccelStreamProfile(handle);
                case StreamType.OB_STREAM_GYRO:
                    return new GyroStreamProfile(handle);
                case StreamType.OB_STREAM_LIDAR:
                    return new LiDARStreamProfile(handle);
                default:
                    return new StreamProfile(handle);
            }
        }
    }
}