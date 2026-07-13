using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System;

namespace Orbbec
{
    public class CoordinateTransformHelper
    {
        public static bool Transformation2dto2d(Point2f sourcePixel, float depthValue, CameraIntrinsic sourceIntrinsic,
            CameraDistortion sourceDistortion, CameraIntrinsic targetIntrinsic, CameraDistortion targetDistortion,
            Extrinsic extrinsicD2C, ref Point2f targetPixel)
        {
            IntPtr error = IntPtr.Zero;
            bool result = obNative.ob_transformation_2d_to_2d(sourcePixel, depthValue, sourceIntrinsic, sourceDistortion,
                targetIntrinsic, targetDistortion, extrinsicD2C, ref targetPixel, ref error);
            NativeException.HandleError(error);
            return result;
        }

        public static bool Transformation2dto3d(Point2f sourcePixel, float depthValue, CameraIntrinsic sourceIntrinsic,
            Extrinsic extrinsicD2C, ref Point3f targetPixel)
        {
            IntPtr error = IntPtr.Zero;
            bool result = obNative.ob_transformation_2d_to_3d(sourcePixel, depthValue, sourceIntrinsic, 
                extrinsicD2C, ref targetPixel, ref error);
            NativeException.HandleError(error);
            return result;
        }

        public static bool Transformation3dto3d(Point3f sourcePixel, Extrinsic extrinsicD2C, ref Point3f targetPixel)
        {
            IntPtr error = IntPtr.Zero;
            bool result = obNative.ob_transformation_3d_to_3d(sourcePixel, extrinsicD2C, ref targetPixel, ref error);
            NativeException.HandleError(error);
            return result;
        }

        public static bool Transformation3dto2d(Point3f sourcePixel, CameraIntrinsic sourceIntrinsic, CameraDistortion sourceDistortion,
            Extrinsic extrinsicD2C, ref Point2f targetPixel)
        {
            IntPtr error = IntPtr.Zero;
            bool result = obNative.ob_transformation_3d_to_2d(sourcePixel, sourceIntrinsic, sourceDistortion,
                extrinsicD2C, ref targetPixel, ref error);
            NativeException.HandleError(error);
            return result;
        }

        /**
        * \if English
        * @brief Transform 3D point from source to target using calibration parameters
        * \else
        * @brief 使用标定参数将3D点从源坐标系转换到目标坐标系
        * \endif
        */
        public static bool Calibration3dTo3d(CalibrationParam calibrationParam, Point3f sourcePoint3f, SensorType sourceSensorType,
            SensorType targetSensorType, out Point3f targetPoint3f)
        {
            IntPtr error = IntPtr.Zero;
            bool result = obNative.ob_calibration_3d_to_3d(calibrationParam, sourcePoint3f, sourceSensorType, targetSensorType, out targetPoint3f, ref error);
            NativeException.HandleError(error);
            return result;
        }

        /**
        * \if English
        * @brief Transform 2D pixel with depth to 3D point using calibration parameters
        * \else
        * @brief 使用标定参数将2D像素（带深度值）转换为3D点
        * \endif
        */
        public static bool Calibration2dTo3d(CalibrationParam calibrationParam, Point2f sourcePoint2f, float sourceDepthPixelValue,
            SensorType sourceSensorType, SensorType targetSensorType, out Point3f targetPoint3f)
        {
            IntPtr error = IntPtr.Zero;
            bool result = obNative.ob_calibration_2d_to_3d(calibrationParam, sourcePoint2f, sourceDepthPixelValue, sourceSensorType, targetSensorType, out targetPoint3f, ref error);
            NativeException.HandleError(error);
            return result;
        }

        /**
        * \if English
        * @brief Transform 3D point to 2D pixel using calibration parameters
        * \else
        * @brief 使用标定参数将3D点转换为2D像素
        * \endif
        */
        public static bool Calibration3dTo2d(CalibrationParam calibrationParam, Point3f sourcePoint3f, SensorType sourceSensorType,
            SensorType targetSensorType, out Point2f targetPoint2f)
        {
            IntPtr error = IntPtr.Zero;
            bool result = obNative.ob_calibration_3d_to_2d(calibrationParam, sourcePoint3f, sourceSensorType, targetSensorType, out targetPoint2f, ref error);
            NativeException.HandleError(error);
            return result;
        }

        /**
        * \if English
        * @brief Transform 2D pixel with depth to 2D pixel using calibration parameters
        * \else
        * @brief 使用标定参数将2D像素（带深度值）转换为2D像素
        * \endif
        */
        public static bool Calibration2dTo2d(CalibrationParam calibrationParam, Point2f sourcePoint2f, float sourceDepthPixelValue,
            SensorType sourceSensorType, SensorType targetSensorType, out Point2f targetPoint2f)
        {
            targetPoint2f = new Point2f();
            IntPtr error = IntPtr.Zero;
            bool result = obNative.ob_calibration_2d_to_2d(calibrationParam, sourcePoint2f, sourceDepthPixelValue, sourceSensorType, targetSensorType, targetPoint2f, ref error);
            NativeException.HandleError(error);
            return result;
        }

        /**
        * \if English
        * @brief Transform depth frame to color camera coordinate system
        * \else
        * @brief 将深度帧对齐到彩色相机坐标系
        * \endif
        */
        public static Frame TransformationDepthFrameToColorCamera(Device device, Frame depthFrame, UInt32 targetColorCameraWidth, UInt32 targetColorCameraHeight)
        {
            IntPtr error = IntPtr.Zero;
            IntPtr result = obNative.transformation_depth_frame_to_color_camera(device.GetNativeHandle().Ptr, depthFrame.GetNativeHandle().Ptr, targetColorCameraWidth, targetColorCameraHeight, ref error);
            NativeException.HandleError(error);
            return new Frame(result);
        }
    }
}
