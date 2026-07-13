using System;
using System.Runtime.InteropServices;

namespace Orbbec
{
    public class PointCloudHelper
    {
        public static bool SavePointcloudToPly(string fileName, Frame frame, bool saveBinary, bool useMesh, float meshThreshold)
        {
            IntPtr error = IntPtr.Zero;
            bool result = obNative.ob_save_pointcloud_to_ply(fileName, frame == null ? IntPtr.Zero : frame.GetNativeHandle().Ptr,
                saveBinary, useMesh, meshThreshold, ref error);
            NativeException.HandleError(error);
            return result;
        }

        /**
        * \if English
        * @brief Save LiDAR point cloud data to PLY file
        *
        * @param fileName Output file name
        * @param frame LiDAR points frame
        * @param saveBinary Whether to save in binary format
        * @return bool Returns true if save successful
        * \else
        * @brief 保存LiDAR点云数据到PLY文件
        *
        * @param fileName 输出文件名
        * @param frame LiDAR点云帧
        * @param saveBinary 是否以二进制格式保存
        * @return bool 成功返回true
        * \endif
        */
        public static bool SaveLiDARPointcloudToPly(string fileName, Frame frame, bool saveBinary)
        {
            IntPtr error = IntPtr.Zero;
            bool result = obNative.ob_save_lidar_pointcloud_to_ply(fileName, frame == null ? IntPtr.Zero : frame.GetNativeHandle().Ptr,
                saveBinary, ref error);
            NativeException.HandleError(error);
            return result;
        }

        /**
        * \if English
        * @brief Initialize XY tables for point cloud transformation
        * \else
        * @brief 初始化点云转换的XY表
        * \endif
        */
        public static bool TransformationInitXYTables(CalibrationParam calibrationParam, SensorType sensorType, IntPtr data, ref UInt32 dataSize, IntPtr xyTables)
        {
            IntPtr error = IntPtr.Zero;
            IntPtr dataSizePtr = Marshal.AllocHGlobal(sizeof(UInt32));
            Marshal.WriteInt32(dataSizePtr, (int)dataSize);
            bool result = obNative.transformation_init_xy_tables(calibrationParam, sensorType, data, dataSizePtr, xyTables, ref error);
            dataSize = (UInt32)Marshal.ReadInt32(dataSizePtr);
            Marshal.FreeHGlobal(dataSizePtr);
            NativeException.HandleError(error);
            return result;
        }

        /**
        * \if English
        * @brief Transform depth image to point cloud using XY tables
        * \else
        * @brief 使用XY表将深度图像转换为点云
        * \endif
        */
        public static void TransformationDepthToPointCloud(IntPtr xyTables, IntPtr depthImageData, IntPtr pointCloudData)
        {
            IntPtr error = IntPtr.Zero;
            obNative.transformation_depth_to_pointcloud(xyTables, depthImageData, pointCloudData, ref error);
            NativeException.HandleError(error);
        }

        /**
        * \if English
        * @brief Transform depth and color images to RGBD point cloud using XY tables
        * \else
        * @brief 使用XY表将深度和彩色图像转换为RGBD点云
        * \endif
        */
        public static void TransformationDepthToRGBDPointCloud(IntPtr xyTables, IntPtr depthImageData, IntPtr colorImageData, IntPtr pointCloudData)
        {
            IntPtr error = IntPtr.Zero;
            obNative.transformation_depth_to_rgbd_pointcloud(xyTables, depthImageData, colorImageData, pointCloudData, ref error);
            NativeException.HandleError(error);
        }
    }
}