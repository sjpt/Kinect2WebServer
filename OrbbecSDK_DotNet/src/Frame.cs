using System;
using System.Runtime.InteropServices;

namespace Orbbec
{
    public delegate void FrameDestroyCallback(byte[] buffer);

    public class Frame : IDisposable
    {
        protected NativeHandle _handle;

        internal Frame(IntPtr handle)
        {
            _handle = new NativeHandle(handle, Delete);
        }

        internal Frame(NativeHandle handle)
        {
            _handle = handle;
            _handle.Retain();
        }

        internal NativeHandle GetNativeHandle()
        {
            return _handle;
        }

        public T As<T>() where T : Frame
        {
            switch (GetFrameType())
            {
                case FrameType.OB_FRAME_VIDEO:
                    return new VideoFrame(_handle) as T;
                case FrameType.OB_FRAME_IR:
                case FrameType.OB_FRAME_IR_LEFT:
                case FrameType.OB_FRAME_IR_RIGHT:
                    return new IRFrame(_handle) as T;
                case FrameType.OB_FRAME_COLOR:
                case FrameType.OB_FRAME_COLOR_LEFT:
                case FrameType.OB_FRAME_COLOR_RIGHT:
                    return new ColorFrame(_handle) as T;
                case FrameType.OB_FRAME_DEPTH:
                    return new DepthFrame(_handle) as T;
                case FrameType.OB_FRAME_ACCEL:
                    return new AccelFrame(_handle) as T;
                case FrameType.OB_FRAME_SET:
                    return new Frameset(_handle) as T;
                case FrameType.OB_FRAME_POINTS:
                    return new PointsFrame(_handle) as T;
                case FrameType.OB_FRAME_GYRO:
                    return new GyroFrame(_handle) as T;
                case FrameType.OB_FRAME_CONFIDENCE:
                    return new ConfidenceFrame(_handle) as T;
            }
            return null;
        }

        public Frame Copy()
        {
            return new Frame(_handle);
        }

        /**
        * \if English
        * @brief Get the sequence number of the frame
        *
        * @return UInt64 returns the sequence number of the frame
        * \else
        * @brief 获取帧的序号
        *
        * @return UInt64 返回帧的序号
        * \endif
        */
        public UInt64 GetIndex()
        {
            IntPtr error = IntPtr.Zero;
            UInt64 index = obNative.ob_frame_get_index(_handle.Ptr, ref error);
            NativeException.HandleError(error);
            return index;
        }

        /**
        * \if English
        * @brief Get the format of the frame
        *
        * @return Format returns the format of the frame
        * \else
        * @brief 获取帧的格式
        *
        * @return Format 返回帧的格式
        * \endif
        */
        public Format GetFormat()
        {
            IntPtr error = IntPtr.Zero;
            Format format = obNative.ob_frame_get_format(_handle.Ptr, ref error);
            NativeException.HandleError(error);
            return format;
        }

        /**
        * \if English
        * @brief Get the type of frame
        *
        * @return FrameType returns the type of frame
        * \else
        * @brief 获取帧的类型
        *
        * @return FrameType 返回帧的类型
        * \endif
        */
        public FrameType GetFrameType()
        {
            IntPtr error = IntPtr.Zero;
            FrameType frameType = obNative.ob_frame_get_type(_handle.Ptr, ref error);
            NativeException.HandleError(error);
            return frameType;
        }

        /**
        * \if English
        * @brief Get the hardware timestamp of the frame
        *
        * @return UInt64 returns the time stamp of the frame hardware
        * \else
        * @brief 获取帧的硬件时间戳
        *
        * @return UInt64 返回帧硬件的时间戳
        * \endif
        */
        public UInt64 GetTimeStamp()
        {
            IntPtr error = IntPtr.Zero;
            UInt64 timestamp = obNative.ob_frame_time_stamp(_handle.Ptr, ref error);
            NativeException.HandleError(error);
            return timestamp;
        }

        /**
        * \if English
        * @brief Get the hardware timestamp of the frame us
        *
        * @return uint64_t returns the time stamp of the frame hardware, unit us
        * \else
        * @brief 获取帧的硬件时间戳
        *
        * @return uint64_t 返回帧硬件的时间戳
        * \endif
        */
        public UInt64 GetTimeStampUs()
        {
            IntPtr error = IntPtr.Zero;
            UInt64 timestamp = obNative.ob_frame_get_timestamp_us(_handle.Ptr, ref error);
            NativeException.HandleError(error);
            return timestamp;
        }

        /**
        * \if English
        * @brief Get the global timestamp of the frame in microseconds.
        * @brief The global timestamp is the time point when the frame was captured by the device, and has been converted to the host clock domain. The
        * conversion process base on the device timestamp and can eliminate the timer drift of the device
        *
        * @attention The global timestamp disable by default. If global timestamp is not enabled, the function will return 0. To enable the global timestamp,
        * please call @ref Device.EnableGlobalTimestamp() function.
        * @attention Only some devices support getting the global timestamp. Check the device support status by @ref Device.IsGlobalTimestampSupported() function.
        *
        * @return UInt64 The global timestamp of the frame in microseconds.
        * \else
        * @brief 获取帧的全局时间戳（微秒）
        * @brief 全局时间戳是帧被设备捕获的时间点，并已转换为主机时钟域。转换过程基于设备时间戳，可以消除设备的计时器漂移
        *
        * @attention 全局时间戳默认禁用。如果未启用全局时间戳，函数将返回0。要启用全局时间戳，请调用 @ref Device.EnableGlobalTimestamp() 函数。
        * @attention 仅部分设备支持获取全局时间戳。请通过 @ref Device.IsGlobalTimestampSupported() 函数检查设备支持状态。
        *
        * @return UInt64 帧的全局时间戳，单位为微秒
        * \endif
        */
        public UInt64 GetGlobalTimeStampUs()
        {
            IntPtr error = IntPtr.Zero;
            UInt64 timestamp = obNative.ob_frame_get_global_timestamp_us(_handle.Ptr, ref error);
            NativeException.HandleError(error);
            return timestamp;
        }

        /**
        * \if English
        * @brief Set the system timestamp of the frame in microseconds
        *
        * @param systemTimestampUs The system timestamp to set
        * \else
        * @brief 设置帧的系统时间戳（微秒）
        *
        * @param systemTimestampUs 要设置的系统时间戳
        * \endif
        */
        public void SetSystemTimestampUs(UInt64 systemTimestampUs)
        {
            IntPtr error = IntPtr.Zero;
            obNative.ob_frame_set_timestamp_us(_handle.Ptr, systemTimestampUs, ref error);
            NativeException.HandleError(error);
        }

        /**
        * \if English
        * @brief Get frame system timestamp
        *
        * @return UInt64 returns the time stamp of the frame hardware
        * \else
        * @brief 获取帧的系统时间戳
        *
        * @return UInt64 返回帧的系统时间戳
        * \endif
        */
        public UInt64 GetSystemTimeStamp()
        {
            IntPtr error = IntPtr.Zero;
            UInt64 sysTimestamp = obNative.ob_frame_system_time_stamp(_handle.Ptr, ref error);
            NativeException.HandleError(error);
            return sysTimestamp;
        }

        /**
        * @brief 获取帧数据
        * @param data 获取到的帧数据
        */
        public void CopyData(ref Byte[] data)
        {
            IntPtr error = IntPtr.Zero;
            IntPtr dataPtr = obNative.ob_frame_get_data(_handle.Ptr, ref error);
            NativeException.HandleError(error);
            Marshal.Copy(dataPtr, data, 0, data.Length);
        }

        /**
        * @brief 获取帧数据
        * @return IntPtr 获取数据在非托管内存的原始指针
        */
        public IntPtr GetDataPtr()
        {
            IntPtr error = IntPtr.Zero;
            IntPtr dataPtr = obNative.ob_frame_get_data(_handle.Ptr, ref error);
            NativeException.HandleError(error);
            return dataPtr;
        }

        /**
        * \if English
        * @brief Get the frame data size
        *
        * @return UInt32 returns the frame data size
        * If it is point cloud data, it returns the number of bytes occupied by all point sets. If you need to find the number of points, you need to divide the
        * dataSize by the structure size of the corresponding point type. \else
        * @brief 获取帧数据大小
        *
        * @return UInt32 返回帧数据的大小
        * 如果是点云数据返回的是所有点集合占的字节数，若需要求出点的个数需要将dataSize除以对应的点类型的结构体大小
        * \endif
        */
        public UInt32 GetDataSize()
        {
            IntPtr error = IntPtr.Zero;
            UInt32 dataSize = obNative.ob_frame_get_data_size(_handle.Ptr, ref error);
            NativeException.HandleError(error);
            return dataSize;
        }

        public static Frame Create(FrameType frameType, Format format, UInt32 dataSize)
        {
            IntPtr error = IntPtr.Zero;
            IntPtr handle = obNative.ob_create_frame(frameType, format, dataSize, ref error);
            NativeException.HandleError(error);
            return new Frame(handle);
        }

        public T CreateFrameFromOtherFrame<T>(bool shouldCopyData) where T : Frame
        {
            IntPtr error = IntPtr.Zero;
            IntPtr handle = obNative.ob_create_frame_from_other_frame(_handle.Ptr, shouldCopyData, ref error);
            NativeException.HandleError(error);
            return new Frame(handle) as T;
        }

        public static Frame CreateFrameFromStreamProfile(StreamProfile streamProfile)
        {
            IntPtr error = IntPtr.Zero;
            IntPtr handle = obNative.ob_create_frame_from_stream_profile(streamProfile.GetNativeHandle().Ptr, ref error);
            NativeException.HandleError(error);
            return new Frame(handle);
        }

        /*        public Frame CreateFrameFromBuffer(FrameType frameType, Format format, IntPtr buffer, UInt32 bufferSize, FrameDestroyCallback callback, IntPtr userData)
                {
                    IntPtr error = IntPtr.Zero;
                    NativeFrameDestroyCallback _nativeCallback = new NativeFrameDestroyCallback((buffer, userData) => {
                        callback();
                    });
                    IntPtr handle = obNative.ob_create_frame_from_buffer(frameType, format, buffer, bufferSize, _nativeCallback, userData, ref error);
                    if (error != IntPtr.Zero)
                    {
                        throw new NativeException(new Error(error));
                    }
                    return new Frame(handle);
                }*/

        public void CopyInfo(Frame dstFrame)
        {
            IntPtr error = IntPtr.Zero;
            obNative.ob_frame_copy_info(_handle.Ptr, dstFrame.GetNativeHandle().Ptr, ref error);
            NativeException.HandleError(error);
        }

        public void UpdateData(IntPtr data, UInt32 dataSize)
        {
            IntPtr error = IntPtr.Zero;
            obNative.ob_frame_update_data(_handle.Ptr, data, dataSize, ref error);
            NativeException.HandleError(error);
        }

        public void UpdateMetaData(IntPtr metadata, UInt32 metaDataSize)
        {
            IntPtr error = IntPtr.Zero;
            obNative.ob_frame_update_metadata(_handle.Ptr, metadata, metaDataSize, ref error);
            NativeException.HandleError(error);
        }

        public void SetStreamProfile(StreamProfile streamProfile)
        {
            IntPtr error = IntPtr.Zero;
            obNative.ob_frame_set_stream_profile(_handle.Ptr, streamProfile.GetNativeHandle().Ptr, ref error);
            NativeException.HandleError(error);
        }

        public StreamProfile GetStreamProfile()
        {
            IntPtr error = IntPtr.Zero;
            IntPtr handle = obNative.ob_frame_get_stream_profile(_handle.Ptr, ref error);
            NativeException.HandleError(error);
            return new StreamProfile(handle);
        }

        /**
        * \if English
        * @brief Get the sensor from which the frame was captured
        *
        * @return Sensor returns the sensor object
        * \else
        * @brief 获取捕获该帧的传感器
        *
        * @return Sensor 返回传感器对象
        * \endif
        */
        public Sensor GetSensor()
        {
            IntPtr error = IntPtr.Zero;
            IntPtr handle = obNative.ob_frame_get_sensor(_handle.Ptr, ref error);
            if (handle == IntPtr.Zero)
            {
                return null;
            }
            NativeException.HandleError(error);
            return new Sensor(handle);
        }

        /**
        * \if English
        * @brief Get the device from which the frame was captured
        *
        * @return Device returns the device object
        * \else
        * @brief 获取捕获该帧的设备
        *
        * @return Device 返回设备对象
        * \endif
        */
        public Device GetDevice()
        {
            IntPtr error = IntPtr.Zero;
            IntPtr handle = obNative.ob_frame_get_device(_handle.Ptr, ref error);
            if (handle == IntPtr.Zero)
            {
                return null;
            }
            NativeException.HandleError(error);
            return new Device(handle);
        }

        /**
        * \if English
        * @brief Get the metadata of the frame
        *
        * @return Byte[] returns the metadata of the frame
        * \else
        * @brief 获取帧的元数据
        *
        * @return Byte[] 返回帧的元数据
        * \endif
        */
        public Byte[] GetMetadata()
        {
            IntPtr error = IntPtr.Zero;
            IntPtr data = obNative.ob_frame_get_metadata(_handle.Ptr, ref error);
            NativeException.HandleError(error);
            UInt32 dataSize = GetMetadataSize();
            Byte[] buffer = new Byte[dataSize];
            Marshal.Copy(data, buffer, 0, (int)dataSize);
            return buffer;
        }

        /**
        * \if English
        * @brief Get the metadata size of the frame
        *
        * @return UInt32 returns the metadata size of the frame
        * \else
        * @brief 获取帧的元数据大小
        *
        * @return UInt32 返回帧的元数据大小
        * \endif
        */
        public UInt32 GetMetadataSize()
        {
            IntPtr error = IntPtr.Zero;
            UInt32 size = obNative.ob_frame_get_metadata_size(_handle.Ptr, ref error);
            NativeException.HandleError(error);
            return size;
        }

        /**
        * \if English
        * @brief Check if the frame object has metadata of a given type
        *
        * @param type The metadata type. refer to @ref FrameMetadataType
        * @return bool The result
        * \else
        * @brief 检查帧对象是否具有给定类型的元数据
        *
        * @param type 元数据类型。请参考@ref FrameMetadataType
        * @return bool 结果
        * \endif
        */
        public bool HasMetadata(FrameMetadataType type)
        {
            IntPtr error = IntPtr.Zero;
            bool result = obNative.ob_frame_has_metadata(_handle.Ptr, (uint)type, ref error);
            NativeException.HandleError(error);
            return result;
        }

        /**
        * \if English
        * @brief Get the metadata value
        *
        * @param type The metadata type. refer to @ref FrameMetadataType
        * @return long The result
        * \else
        * @brief 获取元数据值
        *
        * @param type 元数据类型。请参考@ref FrameMetadataType
        * @return long 元数据值
        * \endif
        */
        public long GetMetadataValue(FrameMetadataType type)
        {
            IntPtr error = IntPtr.Zero;
            long value = obNative.ob_frame_get_metadata_value(_handle.Ptr, (uint)type, ref error);
            NativeException.HandleError(error);
            return value;
        }

        internal void Delete(IntPtr handle)
        {
            IntPtr error = IntPtr.Zero;
            obNative.ob_delete_frame(handle, ref error);
            NativeException.HandleError(error);
        }

        public void Dispose()
        {
            _handle.Dispose();
        }
    }

    public class VideoFrame : Frame
    {
        internal VideoFrame(IntPtr handle) : base(handle)
        {
        }

        internal VideoFrame(NativeHandle handle) : base(handle)
        {
        }

        /**
        * \if English
        * @brief Get frame width
        *
        * @return UInt32 returns the width of the frame
        * \else
        * @brief 获取帧的宽
        *
        * @return UInt32 返回帧的宽
        * \endif
        */
        public UInt32 GetWidth()
        {
            IntPtr error = IntPtr.Zero;
            return obNative.ob_video_frame_get_width(_handle.Ptr, ref error);
        }

        /**
        * \if English
        * @brief Get frame height
        *
        * @return UInt32 returns the height of the frame
        * \else
        * @brief 获取帧的高
        *
        * @return UInt32 返回帧的高
        * \endif
        */
        public UInt32 GetHeight()
        {
            IntPtr error = IntPtr.Zero;
            return obNative.ob_video_frame_get_height(_handle.Ptr, ref error);
        }

        /**
        * \if English
        * @brief Get the effective number of pixels (such as Y16 format frame, but only the lower 10 bits are valid bits, and the upper 6 bits are filled with 0)
        * @attention Only valid for Y8/Y10/Y11/Y12/Y14/Y16 format
        *
        * @return uint8_t returns the effective number of pixels in the pixel, or 0 if it is an unsupported format
        * \else
        * @brief 获取像素有效位数（如Y16格式帧，每个像素占16bit，但实际只有低10位是有效位，高6位填充0）
        * @attention 仅对Y8/Y10/Y11/Y12/Y14/Y16格式有效
        *
        * @return uint8_t 返回像素有效位数，如果是不支持的格式，返回0
        * \endif
        */
        public byte PixelAvailableBitSize()
        {
            IntPtr error = IntPtr.Zero;
            return obNative.ob_video_frame_get_pixel_available_bit_size(_handle.Ptr, ref error);
        }

        public static VideoFrame Create(FrameType frameType, Format format, UInt32 width, UInt32 height, UInt32 strideBytes)
        {
            IntPtr error = IntPtr.Zero;
            IntPtr handle = obNative.ob_create_video_frame(frameType, format, width, height, strideBytes, ref error);
            NativeException.HandleError(error);
            return new VideoFrame(handle);
        }

        public PixelType GetPixelType()
        {
            IntPtr error = IntPtr.Zero;
            PixelType type = obNative.ob_video_frame_get_pixel_type(_handle.Ptr, ref error);
            NativeException.HandleError(error);
            return type;
        }

        public void SetPixelType(PixelType pixelType)
        {
            IntPtr error = IntPtr.Zero;
            obNative.ob_video_frame_set_pixel_type(_handle.Ptr, pixelType, ref error);
            NativeException.HandleError(error);
        }

        public void SetPixelAvailableBitSize(UInt16 bitSize)
        {
            IntPtr error = IntPtr.Zero;
            obNative.ob_video_frame_set_pixel_available_bit_size(_handle.Ptr, bitSize, ref error);
            NativeException.HandleError(error);
        }
    }

    public class ColorFrame : VideoFrame
    {
        internal ColorFrame(IntPtr handle) : base(handle)
        {
        }

        internal ColorFrame(NativeHandle handle) : base(handle)
        {
        }
    }

    public class DepthFrame : VideoFrame
    {
        internal DepthFrame(IntPtr handle) : base(handle)
        {
        }

        internal DepthFrame(NativeHandle handle) : base(handle)
        {
        }

        /**
        * \if English
        * @brief Get the value scale of the depth frame, the unit is mm/step,
        *        such as valueScale=0.1, and a certain coordinate pixel value is pixelValue=10000,
        *        then the depth value value = pixelValue*valueScale = 10000*0.1=1000mm.
        *
        * @return float
        * \else
        * @brief 获取深度帧的值刻度，单位为 mm/step，
        *      如valueScale=0.1, 某坐标像素值为pixelValue=10000，
        *     则表示深度值value = pixelValue*valueScale = 10000*0.1=1000mm。
        *
        * @return float
        * \endif
        */
        public float GetValueScale()
        {
            IntPtr error = IntPtr.Zero;
            return obNative.ob_depth_frame_get_value_scale(_handle.Ptr, ref error);
        }

        /**
        * \if English
        * @brief Get the coordinate value scale of the depth frame
        * @note Alias for GetValueScale()
        * \else
        * @brief 获取深度帧的坐标值刻度
        * @note GetValueScale() 的别名
        * \endif
        */
        public float GetCoordinateValueScale()
        {
            return GetValueScale();
        }

        /**
        * \if English
        * @brief Set the value scale of the depth frame
        *
        * @param valueScale The value scale to set
        * \else
        * @brief 设置深度帧的值刻度
        *
        * @param valueScale 要设置的值刻度
        * \endif
        */
        public void SetValueScale(float valueScale)
        {
            IntPtr error = IntPtr.Zero;
            obNative.ob_depth_frame_set_value_scale(_handle.Ptr, valueScale, ref error);
            NativeException.HandleError(error);
        }
    }

    public class IRFrame : VideoFrame
    {
        internal IRFrame(IntPtr handle) : base(handle)
        {
        }

        internal IRFrame(NativeHandle handle) : base(handle)
        {
        }
    }

    public class PointsFrame : Frame
    {
        internal PointsFrame(IntPtr handle) : base(handle)
        {
        }

        internal PointsFrame(NativeHandle handle) : base(handle)
        {
        }

        /**
        * \if English
        * @brief Get the point position value scale of the points frame. the point position value of points frame is multiplied by the scale to give a position
        * value in millimeter. such as scale=0.1, The x-coordinate value of a point is x = 10000, which means that the actual x-coordinate value = x*scale =
        * 10000*0.1 = 1000mm.
        *
        * @param[in] frame Frame object
        * @param[out] error Log error messages
        * @return float position value scale
        * \else
        * @brief 获取点云帧的点坐标值缩放系数，点坐标值乘以缩放系数后，可以得到单位为毫米的坐标值； 如scale=0.1, 某个点的x坐标值为x=10000，
        *     则表示实际x坐标value = x*scale = 10000*0.1=1000mm。
        *
        * @return float 缩放系数
        * \endif
        */
        public float GetPositionValueScale()
        {
            IntPtr error = IntPtr.Zero;
            return obNative.ob_points_frame_get_coordinate_value_scale(_handle.Ptr, ref error);
        }

        /**
        * \if English
        * @brief Get point cloud frame width
        *
        * @return uint32_t return the point cloud frame width
        * \else
        * @brief 获取点云帧的宽
        *
        * @return uint32_t 返回点云帧的宽
        * \endif
        */
        public UInt32 GetWidth()
        {
            IntPtr error = IntPtr.Zero;
            return obNative.ob_point_cloud_frame_get_width(_handle.Ptr, ref error);
        }

        /**
        * \if English
        * @brief Get point cloud frame height
        *
        * @return uint32_t return the point cloud frame height
        * \else
        * @brief 获取点云帧的高
        *
        * @return uint32_t 返回点云帧的高
        * \endif
        */
        public UInt32 GetHeight()
        {
            IntPtr error = IntPtr.Zero;
            return obNative.ob_point_cloud_frame_get_height(_handle.Ptr, ref error);
        }

        /**
        * \if English
        * @brief Get the coordinate value scale of the points frame
        * @note Alias for GetPositionValueScale()
        * \else
        * @brief 获取点云帧的坐标值缩放系数
        * @note GetPositionValueScale() 的别名
        * \endif
        */
        public float GetCoordinateValueScale()
        {
            return GetPositionValueScale();
        }
    }

    public class AccelFrame : Frame
    {
        internal AccelFrame(IntPtr handle) : base(handle)
        {
        }

        internal AccelFrame(NativeHandle handle) : base(handle)
        {
        }

        /**
        * @brief 获取帧的加速度值
        * @return AccelValue 返回加速度值
        */
        public AccelValue GetAccelValue()
        {
            IntPtr error = IntPtr.Zero;
            AccelValue accelValue = obNative.ob_accel_frame_get_value(_handle.Ptr, ref error);
            return accelValue;
        }

        /**
        * @brief 获取帧采样时的温度
        * @return float 返回温度值
        */
        public float GetTemperature()
        {
            IntPtr error = IntPtr.Zero;
            return obNative.ob_accel_frame_get_temperature(_handle.Ptr, ref error);
        }
    }

    public class GyroFrame : Frame
    {
        internal GyroFrame(IntPtr handle) : base(handle)
        {
        }

        internal GyroFrame(NativeHandle handle) : base(handle)
        {
        }

        /**
        * @brief 获取陀螺仪帧数据
        * @return GyroValue 返回陀螺仪的值
        */
        public GyroValue GetGyroValue()
        {
            IntPtr error = IntPtr.Zero;
            GyroValue gyroValue = obNative.ob_gyro_frame_get_value(_handle.Ptr, ref error);
            return gyroValue;
        }

        /**
        * @brief 获取帧采样时的温度
        * @return float 返回温度值
        */
        public float GetTemperature()
        {
            IntPtr error = IntPtr.Zero;
            return obNative.ob_gyro_frame_get_temperature(_handle.Ptr, ref error);
        }
    }

    public class Frameset : Frame
    {
        internal Frameset(IntPtr handle) : base(handle)
        {
        }

        internal Frameset(NativeHandle handle) : base(handle)
        {
        }

        /**
        * \if English
        * @brief Get frame count
        *
        * @return UInt32 returns the number of frames
        * \else
        * @brief 帧集合中包含的帧数量
        *
        * @return UInt32 返回帧的数量
        * \endif
        */
        public UInt32 GetFrameCount()
        {
            IntPtr error = IntPtr.Zero;
            return obNative.ob_frameset_get_count(_handle.Ptr, ref error);
        }

        /**
        * \if English
        * @brief Get depth frame
        *
        * @return DepthFrame returns the depth frame
        * \else
        * @brief 获取深度帧
        *
        * @return DepthFrame 返回深度帧
        * \endif
        */
        public DepthFrame GetDepthFrame()
        {
            IntPtr error = IntPtr.Zero;
            IntPtr handle = obNative.ob_frameset_get_depth_frame(_handle.Ptr, ref error);
            if (handle == IntPtr.Zero)
            {
                return null;
            }
            return new DepthFrame(handle);
        }

        /**
        * \if English
        * @brief Get color frame
        *
        * @return ColorFrame returns the color frame
        * \else
        * @brief 获取彩色帧
        *
        * @return ColorFrame 返回彩色帧
        * \endif
        */
        public ColorFrame GetColorFrame()
        {
            IntPtr error = IntPtr.Zero;
            IntPtr handle = obNative.ob_frameset_get_color_frame(_handle.Ptr, ref error);
            if (handle == IntPtr.Zero)
            {
                return null;
            }
            return new ColorFrame(handle);
        }

        /**
        * \if English
        * @brief Get infrared frame
        *
        * @return IRFrame returns infrared frame
        * \else
        * @brief 获取红外帧
        *
        * @return IRFrame 返回红外帧
        * \endif
        */
        public IRFrame GetIRFrame()
        {
            IntPtr error = IntPtr.Zero;
            IntPtr handle = obNative.ob_frameset_get_ir_frame(_handle.Ptr, ref error);
            if (handle == IntPtr.Zero)
            {
                return null;
            }
            return new IRFrame(handle);
        }

        /**
        * \if English
        * @brief Get point cloud frame
        *
        * @return  PointsFrame returns the point cloud data frame
        * \else
        * @brief 获取点云帧
        *
        * @return  PointsFrame 返回点云帧
        * \endif
        */
        public PointsFrame GetPointsFrame()
        {
            IntPtr error = IntPtr.Zero;
            IntPtr handle = obNative.ob_frameset_get_points_frame(_handle.Ptr, ref error);
            if (handle == IntPtr.Zero)
            {
                return null;
            }
            return new PointsFrame(handle);
        }

        /**
        * \if English
        * @brief Get frame by sensor type
        *
        * @param frameType  Type of sensor
        * @return std::shared_ptr<Frame> returns the corresponding type of frame
        * \else
        * @brief 通过传感器类型获取帧
        *
        * @param frameType 传感器的类型
        * @return std::shared_ptr<Frame> 返回相应类型的帧
        * \endif
        */
        public Frame GetFrame(FrameType frameType)
        {
            IntPtr error = IntPtr.Zero;
            IntPtr handle = obNative.ob_frameset_get_frame(_handle.Ptr, frameType, ref error);
            if (handle == IntPtr.Zero)
            {
                return null;
            }
            return new Frame(handle);
        }

        /**
        * \if English
        * @brief Get frame by index
        *
        * @param index Frame index in the FrameSet
        * @return Frame returns the frame at the specified index
        * \else
        * @brief 通过索引获取帧
        *
        * @param index 帧在FrameSet中的索引
        * @return Frame 返回指定索引处的帧
        * \endif
        */
        public Frame GetFrameByIndex(int index)
        {
            IntPtr error = IntPtr.Zero;
            IntPtr handle = obNative.ob_frameset_get_frame_by_index(_handle.Ptr, index, ref error);
            if (handle == IntPtr.Zero)
            {
                return null;
            }
            return new Frame(handle);
        }

        public void PushFrame(Frame frame)
        {
            IntPtr error = IntPtr.Zero;
            obNative.ob_frameset_push_frame(_handle.Ptr, frame.GetNativeHandle().Ptr, ref error);
            NativeException.HandleError(error);
        }
    }

    /**
    * \if English
    * @brief Confidence frame class, inherits from VideoFrame
    * \else
    * @brief 置信度帧类，继承自 VideoFrame
    * \endif
    */
    public class ConfidenceFrame : VideoFrame
    {
        internal ConfidenceFrame(IntPtr handle) : base(handle)
        {
        }

        internal ConfidenceFrame(NativeHandle handle) : base(handle)
        {
        }
    }

    /**
    * \if English
    * @brief LiDAR point cloud frame class, inherits from Frame
    * \else
    * @brief LiDAR点云帧类，继承自 Frame
    * \endif
    */
    public class LiDARPointsFrame : Frame
    {
        internal LiDARPointsFrame(IntPtr handle) : base(handle)
        {
        }

        internal LiDARPointsFrame(NativeHandle handle) : base(handle)
        {
        }
    }

    /**
    * \if English
    * @brief Frame factory class for creating frame objects
    * \else
    * @brief 帧工厂类，用于创建帧对象
    * \endif
    */
    public static class FrameFactory
    {
        /**
        * \if English
        * @brief Create a Frame object of a specific type with a given format and data size.
        * \else
        * @brief 创建特定类型的帧对象，指定格式和数据大小
        * \endif
        */
        public static Frame CreateFrame(FrameType frameType, Format format, UInt32 dataSize)
        {
            IntPtr error = IntPtr.Zero;
            IntPtr handle = obNative.ob_create_frame(frameType, format, dataSize, ref error);
            NativeException.HandleError(error);
            return new Frame(handle);
        }

        /**
        * \if English
        * @brief Create a VideoFrame object of a specific type with a given format, width, height, and stride.
        * \else
        * @brief 创建特定类型的视频帧对象，指定格式、宽度、高度和步长
        * \endif
        */
        public static VideoFrame CreateVideoFrame(FrameType frameType, Format format, UInt32 width, UInt32 height, UInt32 stride = 0)
        {
            IntPtr error = IntPtr.Zero;
            IntPtr handle = obNative.ob_create_video_frame(frameType, format, width, height, stride, ref error);
            NativeException.HandleError(error);
            return new VideoFrame(handle);
        }

        /**
        * \if English
        * @brief Create (clone) a frame object based on the specified other frame object.
        * \else
        * @brief 基于指定的其他帧对象创建（克隆）帧对象
        * \endif
        */
        public static Frame CreateFrameFromOtherFrame(Frame otherFrame, bool shouldCopyData = true)
        {
            IntPtr error = IntPtr.Zero;
            IntPtr handle = obNative.ob_create_frame_from_other_frame(otherFrame.GetNativeHandle().Ptr, shouldCopyData, ref error);
            NativeException.HandleError(error);
            return new Frame(handle);
        }

        /**
        * \if English
        * @brief Create a Frame From Stream Profile object
        * \else
        * @brief 从流配置文件创建帧对象
        * \endif
        */
        public static Frame CreateFrameFromStreamProfile(StreamProfile profile)
        {
            IntPtr error = IntPtr.Zero;
            IntPtr handle = obNative.ob_create_frame_from_stream_profile(profile.GetNativeHandle().Ptr, ref error);
            NativeException.HandleError(error);
            return new Frame(handle);
        }

        /**
        * \if English
        * @brief Create a frame object based on an externally created buffer.
        * \else
        * @brief 基于外部创建的缓冲区创建帧对象
        * \endif
        */
        public static Frame CreateFrameFromBuffer(FrameType frameType, Format format, IntPtr buffer, UInt32 bufferSize, FrameDestroyCallback callback)
        {
            IntPtr error = IntPtr.Zero;
            // Note: The callback is handled at native level
            IntPtr handle = obNative.ob_create_frame_from_buffer(frameType, format, buffer, bufferSize, null, IntPtr.Zero, ref error);
            NativeException.HandleError(error);
            return new Frame(handle);
        }

        /**
        * \if English
        * @brief Create a video frame object based on an externally created buffer.
        * \else
        * @brief 基于外部创建的缓冲区创建视频帧对象
        * \endif
        */
        public static VideoFrame CreateVideoFrameFromBuffer(FrameType frameType, Format format, UInt32 width, UInt32 height, IntPtr buffer, UInt32 bufferSize, FrameDestroyCallback callback, UInt32 stride = 0)
        {
            IntPtr error = IntPtr.Zero;
            // Note: The callback is handled at native level
            IntPtr handle = obNative.ob_create_video_frame_from_buffer(frameType, format, width, height, stride, buffer, bufferSize, null, IntPtr.Zero, ref error);
            NativeException.HandleError(error);
            return new VideoFrame(handle);
        }

        /**
        * \if English
        * @brief Create a new FrameSet object.
        * \else
        * @brief 创建新的帧集合对象
        * \endif
        */
        public static Frameset CreateFrameSet()
        {
            IntPtr error = IntPtr.Zero;
            IntPtr handle = obNative.ob_create_frameset(ref error);
            NativeException.HandleError(error);
            return new Frameset(handle);
        }
    }

    /**
    * \if English
    * @brief Frame helper class with utility methods
    * \else
    * @brief 帧辅助工具类
    * \endif
    */
    public static class FrameHelper
    {
        /**
        * \if English
        * @brief Set the device timestamp of the frame in microseconds.
        * \else
        * @brief 设置帧的设备时间戳（微秒）
        * \endif
        */
        public static void SetFrameDeviceTimestampUs(Frame frame, UInt64 deviceTimestampUs)
        {
            IntPtr error = IntPtr.Zero;
            obNative.ob_frame_set_timestamp_us(frame.GetNativeHandle().Ptr, deviceTimestampUs, ref error);
            NativeException.HandleError(error);
        }
    }
}