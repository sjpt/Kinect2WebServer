using Orbbec;

namespace Samples.Common
{
    public enum ConversionType
    {
        Standard,           // Standard conversion (using ImageUtils methods)
        RequireFilter,      // Must use SDK Filter conversion
        NotSupported        // Unsupported format
    }

    public class ImageUtils
    {
        /// <summary>
        /// Y8 (Grayscale 8-bit) to RGB
        /// </summary>
        public static byte[] Y8ToRgb(int width, int height, ReadOnlySpan<byte> y8Data)
        {
            if (y8Data == null)
                throw new ArgumentException("Y8 data is null");

            int pixelCount = width * height;
            int rgbSize = pixelCount * 3;

            if (y8Data.Length < pixelCount)
                throw new ArgumentException("Invalid Y8 data length");

            byte[] rgbData = new byte[rgbSize];

            for (int i = 0, j = 0; i < pixelCount; ++i, j += 3)
            {
                byte y = y8Data[i];
                rgbData[j] = y;
                rgbData[j + 1] = y;
                rgbData[j + 2] = y;
            }

            return rgbData;
        }

        /// <summary>
        /// Y16 (16-bit depth) to RGB
        /// </summary>
        public static byte[] Y16ToRgb(int width, int height, ReadOnlySpan<byte> y16Data, bool normalize = false)
        {
            if (y16Data == null)
                throw new ArgumentException("Y16 data is null");

            int pixelCount = width * height;
            int rgbSize = pixelCount * 3;

            if (y16Data.Length < pixelCount * 2)
                throw new ArgumentException("Invalid Y16 data length");

            byte[] rgbData = new byte[rgbSize];

            if (normalize)
            {
                for (int i = 0, j = 0; i < pixelCount; ++i, j += 3)
                {
                    ushort y = (ushort)(y16Data[i * 2] | (y16Data[i * 2 + 1] << 8));
                    byte value = (byte)(y >> 8);
                    rgbData[j] = value;
                    rgbData[j + 1] = value;
                    rgbData[j + 2] = value;
                }
            }
            else
            {
                for (int i = 0, j = 0; i < pixelCount; ++i, j += 3)
                {
                    byte value = y16Data[i * 2];
                    rgbData[j] = value;
                    rgbData[j + 1] = value;
                    rgbData[j + 2] = value;
                }
            }

            return rgbData;
        }

        /// <summary>
        /// BGR to RGB
        /// </summary>
        public static byte[] BgrToRgb(int width, int height, ReadOnlySpan<byte> bgrData)
        {
            if (bgrData == null)
                throw new ArgumentException("BGR data is null");

            int pixelCount = width * height;
            int expectedSize = pixelCount * 3;

            if (bgrData.Length < expectedSize)
                throw new ArgumentException("Invalid BGR data length");

            byte[] rgbData = new byte[expectedSize];

            for (int i = 0, j = 0; i < pixelCount; ++i, j += 3)
            {
                // BGR -> RGB
                rgbData[j] = bgrData[j + 2];     // R
                rgbData[j + 1] = bgrData[j + 1]; // G
                rgbData[j + 2] = bgrData[j];     // B
            }

            return rgbData;
        }

        /// <summary>
        /// YUY2/YUYV to RGB
        /// </summary>
        public static byte[] Yuy2ToRgb(int width, int height, ReadOnlySpan<byte> yuy2Data)
        {
            if (yuy2Data == null)
                throw new ArgumentException("YUY2 data is null");

            int pixelCount = width * height;
            int rgbSize = pixelCount * 3;
            int expectedYuy2Size = pixelCount * 2;

            if (yuy2Data.Length < expectedYuy2Size)
                throw new ArgumentException($"Invalid YUY2 data length: expected {expectedYuy2Size}, got {yuy2Data.Length}");

            byte[] rgbData = new byte[rgbSize];

            for (int i = 0; i < pixelCount; i += 2)
            {
                // YUY2 format: Y0 U Y1 V (4 bytes for 2 pixels)
                int yuy2Index = i * 2;
                byte y0 = yuy2Data[yuy2Index];
                byte u = yuy2Data[yuy2Index + 1];
                byte y1 = yuy2Data[yuy2Index + 2];
                byte v = yuy2Data[yuy2Index + 3];

                // Convert first pixel
                ConvertYuvToRgb(y0, u, v, out rgbData[i * 3], out rgbData[i * 3 + 1], out rgbData[i * 3 + 2]);

                // Convert second pixel
                ConvertYuvToRgb(y1, u, v, out rgbData[(i + 1) * 3], out rgbData[(i + 1) * 3 + 1], out rgbData[(i + 1) * 3 + 2]);
            }

            return rgbData;
        }

        /// <summary>
        /// UYVY to RGB
        /// </summary>
        public static byte[] UyvyToRgb(int width, int height, ReadOnlySpan<byte> uyvyData)
        {
            if (uyvyData == null)
                throw new ArgumentException("UYVY data is null");

            int pixelCount = width * height;
            int rgbSize = pixelCount * 3;
            int expectedUyvySize = pixelCount * 2;

            if (uyvyData.Length < expectedUyvySize)
                throw new ArgumentException($"Invalid UYVY data length: expected {expectedUyvySize}, got {uyvyData.Length}");

            byte[] rgbData = new byte[rgbSize];

            for (int i = 0; i < pixelCount; i += 2)
            {
                // UYVY format: U Y0 V Y1 (4 bytes for 2 pixels)
                int uyvyIndex = i * 2;
                byte u = uyvyData[uyvyIndex];
                byte y0 = uyvyData[uyvyIndex + 1];
                byte v = uyvyData[uyvyIndex + 2];
                byte y1 = uyvyData[uyvyIndex + 3];

                // Convert first pixel
                ConvertYuvToRgb(y0, u, v, out rgbData[i * 3], out rgbData[i * 3 + 1], out rgbData[i * 3 + 2]);

                // Convert second pixel
                ConvertYuvToRgb(y1, u, v, out rgbData[(i + 1) * 3], out rgbData[(i + 1) * 3 + 1], out rgbData[(i + 1) * 3 + 2]);
            }

            return rgbData;
        }

        /// <summary>
        /// NV12 to RGB
        /// </summary>
        public static byte[] Nv12ToRgb(int width, int height, ReadOnlySpan<byte> nv12Data)
        {
            if (nv12Data == null)
                throw new ArgumentException("NV12 data is null");

            int ySize = width * height;
            int uvSize = ySize / 2;
            int expectedSize = ySize + uvSize;

            if (nv12Data.Length < expectedSize)
                throw new ArgumentException($"Invalid NV12 data length: expected {expectedSize}, got {nv12Data.Length}");

            byte[] rgbData = new byte[ySize * 3];
            ReadOnlySpan<byte> yPlane = nv12Data.Slice(0, ySize);
            ReadOnlySpan<byte> uvPlane = nv12Data.Slice(ySize, uvSize);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int yIndex = y * width + x;
                    byte yValue = yPlane[yIndex];

                    // UV plane is 2x2 subsampled
                    int uvX = x / 2;
                    int uvY = y / 2;
                    int uvIndex = uvY * width + uvX * 2;
                    byte u = uvPlane[uvIndex];
                    byte v = uvPlane[uvIndex + 1];

                    int rgbIndex = yIndex * 3;
                    ConvertYuvToRgb(yValue, u, v, out rgbData[rgbIndex], out rgbData[rgbIndex + 1], out rgbData[rgbIndex + 2]);
                }
            }

            return rgbData;
        }

        /// <summary>
        /// NV21 to RGB
        /// </summary>
        public static byte[] Nv21ToRgb(int width, int height, ReadOnlySpan<byte> nv21Data)
        {
            if (nv21Data == null)
                throw new ArgumentException("NV21 data is null");

            int ySize = width * height;
            int vuSize = ySize / 2;
            int expectedSize = ySize + vuSize;

            if (nv21Data.Length < expectedSize)
                throw new ArgumentException($"Invalid NV21 data length: expected {expectedSize}, got {nv21Data.Length}");

            byte[] rgbData = new byte[ySize * 3];
            ReadOnlySpan<byte> yPlane = nv21Data.Slice(0, ySize);
            ReadOnlySpan<byte> vuPlane = nv21Data.Slice(ySize, vuSize);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int yIndex = y * width + x;
                    byte yValue = yPlane[yIndex];

                    // VU plane is 2x2 subsampled, order is V then U
                    int vuX = x / 2;
                    int vuY = y / 2;
                    int vuIndex = vuY * width + vuX * 2;
                    byte v = vuPlane[vuIndex];
                    byte u = vuPlane[vuIndex + 1];

                    int rgbIndex = yIndex * 3;
                    ConvertYuvToRgb(yValue, u, v, out rgbData[rgbIndex], out rgbData[rgbIndex + 1], out rgbData[rgbIndex + 2]);
                }
            }

            return rgbData;
        }

        /// <summary>
        /// I420 to RGB
        /// </summary>
        public static byte[] I420ToRgb(int width, int height, ReadOnlySpan<byte> i420Data)
        {
            if (i420Data == null)
                throw new ArgumentException("I420 data is null");

            int ySize = width * height;
            int uSize = ySize / 4;
            int vSize = ySize / 4;
            int expectedSize = ySize + uSize + vSize;

            if (i420Data.Length < expectedSize)
                throw new ArgumentException($"Invalid I420 data length: expected {expectedSize}, got {i420Data.Length}");

            byte[] rgbData = new byte[ySize * 3];
            ReadOnlySpan<byte> yPlane = i420Data.Slice(0, ySize);
            ReadOnlySpan<byte> uPlane = i420Data.Slice(ySize, uSize);
            ReadOnlySpan<byte> vPlane = i420Data.Slice(ySize + uSize, vSize);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int yIndex = y * width + x;
                    byte yValue = yPlane[yIndex];

                    // U and V planes are 2x2 subsampled
                    int uvX = x / 2;
                    int uvY = y / 2;
                    int uvIndex = uvY * (width / 2) + uvX;
                    byte u = uPlane[uvIndex];
                    byte v = vPlane[uvIndex];

                    int rgbIndex = yIndex * 3;
                    ConvertYuvToRgb(yValue, u, v, out rgbData[rgbIndex], out rgbData[rgbIndex + 1], out rgbData[rgbIndex + 2]);
                }
            }

            return rgbData;
        }

        /// <summary>
        /// YUV to RGB conversion
        /// </summary>
        private static void ConvertYuvToRgb(byte y, byte u, byte v, out byte r, out byte g, out byte b)
        {
            // YUV to RGB conversion formula
            int yVal = y - 16;
            int uVal = u - 128;
            int vVal = v - 128;

            int rVal = (298 * yVal + 409 * vVal + 128) >> 8;
            int gVal = (298 * yVal - 100 * uVal - 208 * vVal + 128) >> 8;
            int bVal = (298 * yVal + 516 * uVal + 128) >> 8;

            r = (byte)Math.Clamp(rVal, 0, 255);
            g = (byte)Math.Clamp(gVal, 0, 255);
            b = (byte)Math.Clamp(bVal, 0, 255);
        }

        public static byte[] DepthAlignToColor(int colorW, int colorH, ReadOnlySpan<byte> colorData,
            int depthW, int depthH, ReadOnlySpan<byte> depthData, float alpha)
        {
            if (colorData.Length == 0 || depthData.Length == 0)
                throw new ArgumentException("Data is null");

            if (alpha <= 0)
                return colorData.ToArray();
            else if (alpha > 1)
                return depthData.ToArray();

            float scaleW = (float)colorW / depthW;
            float scaleH = (float)colorH / depthH;

            int colorDataSize = colorW * colorH * 3;
            int depthDataSize = depthW * depthH * 3;

            byte[] output = new byte[colorData.Length];
            colorData.CopyTo(output);

            int colorIndex, depthIndex;
            int srcX, srcY;

            for (int j = 0; j < colorH; ++j)
            {
                for (int i = 0; i < colorW; ++i)
                {
                    srcX = (int)(i / scaleW);
                    srcY = (int)(j / scaleH);

                    colorIndex = (j * colorW + i) * 3;
                    depthIndex = (srcY * depthW + srcX) * 3;

                    if (depthIndex + 3 > depthDataSize || colorIndex + 3 > colorDataSize)
                        continue;

                    byte d0 = depthData[depthIndex];
                    byte d1 = depthData[depthIndex + 1];
                    byte d2 = depthData[depthIndex + 2];

                    if (d0 == 0 && d1 == 0 && d2 == 0)
                        continue;

                    output[colorIndex] = (byte)(output[colorIndex] * (1.0f - alpha) + d0 * alpha);
                    output[colorIndex + 1] = (byte)(output[colorIndex + 1] * (1.0f - alpha) + d1 * alpha);
                    output[colorIndex + 2] = (byte)(output[colorIndex + 2] * (1.0f - alpha) + d2 * alpha);
                }
            }

            return output;
        }

        public static byte[] DepthAlignToColor(ReadOnlySpan<byte> colorData, ReadOnlySpan<byte> depthData, float alpha)
        {
            if (colorData.Length == 0 || depthData.Length == 0)
                throw new ArgumentException("Data is null");

            alpha = Math.Clamp(alpha, 0f, 1f);
            if (alpha <= 0f) return colorData.ToArray();
            if (alpha >= 1f) return depthData.ToArray();

            byte[] output = new byte[colorData.Length];
            colorData.CopyTo(output);
            float ialpha = 1f - alpha;
            for (int i = 0; i < output.Length; i++)
            {
                byte d = depthData[i];
                if (d != 0)
                    output[i] = (byte)(output[i] * ialpha + d * alpha);
            }
            return output;
        }

        /// <summary>
        /// Try generic format conversion - for unknown single-channel formats, try treating as Y8 or Y16
        /// </summary>
        public static byte[]? TryGenericConversion(int width, int height, ReadOnlySpan<byte> data)
        {
            int pixelCount = width * height;
            int expectedSize = pixelCount * 2; // Assume 16-bit format

            if (data.Length >= expectedSize)
            {
                // Data size matches 16-bit format, use Y16 conversion
                return Y16ToRgb(width, height, data);
            }
            else if (data.Length >= pixelCount)
            {
                // Data size matches 8-bit format, use Y8 conversion
                return Y8ToRgb(width, height, data);
            }
            else
            {
                // Data size mismatch, return null
                return null;
            }
        }

        /// <summary>
        /// Determine the conversion type and target format for a given frame format
        /// </summary>
        public static (ConversionType type, ConvertFormat? filterFormat) GetConversionInfo(Format format)
        {
            return format switch
            {
                // Formats requiring SDK Filter conversion
                Format.OB_FORMAT_MJPG => (ConversionType.RequireFilter, ConvertFormat.FORMAT_MJPG_TO_RGB),
                Format.OB_FORMAT_YUYV => (ConversionType.RequireFilter, ConvertFormat.FORMAT_YUYV_TO_RGB),
                Format.OB_FORMAT_YUY2 => (ConversionType.RequireFilter, ConvertFormat.FORMAT_YUYV_TO_RGB),
                Format.OB_FORMAT_UYVY => (ConversionType.RequireFilter, ConvertFormat.FORMAT_UYVY_TO_RGB),
                Format.OB_FORMAT_NV12 => (ConversionType.RequireFilter, ConvertFormat.FORMAT_NV12_TO_RGB),
                Format.OB_FORMAT_NV21 => (ConversionType.RequireFilter, ConvertFormat.FORMAT_NV21_TO_RGB),
                Format.OB_FORMAT_I420 => (ConversionType.RequireFilter, ConvertFormat.FORMAT_I420_TO_RGB),
                Format.OB_FORMAT_BGR => (ConversionType.RequireFilter, ConvertFormat.FORMAT_BGR_TO_RGB),
                Format.OB_FORMAT_RGBA => (ConversionType.RequireFilter, ConvertFormat.FORMAT_RGBA_TO_RGB),

                // Compressed formats that require external decoding
                Format.OB_FORMAT_H264 => (ConversionType.NotSupported, null),
                Format.OB_FORMAT_H265 => (ConversionType.NotSupported, null),

                // Standard formats that can be converted directly
                _ => (ConversionType.Standard, null)
            };
        }

        /// <summary>
        /// Convert frame data to RGB format using standard conversion methods
        /// </summary>
        public static byte[]? ConvertFrameToRgb(int width, int height, Format format, ReadOnlySpan<byte> data)
        {
            try
            {
                return format switch
                {
                    // Directly supported formats
                    Format.OB_FORMAT_RGB => data.ToArray(),
                    Format.OB_FORMAT_BGR => BgrToRgb(width, height, data),
                    Format.OB_FORMAT_BGRA => data.ToArray(),
                    Format.OB_FORMAT_RGBA => data.ToArray(),

                    // YUV/Y formats
                    Format.OB_FORMAT_Y8 => Y8ToRgb(width, height, data),
                    Format.OB_FORMAT_Y16 => Y16ToRgb(width, height, data),
                    Format.OB_FORMAT_Y10 => Y16ToRgb(width, height, data),
                    Format.OB_FORMAT_Y11 => Y16ToRgb(width, height, data),
                    Format.OB_FORMAT_Y12 => Y16ToRgb(width, height, data),
                    Format.OB_FORMAT_Y14 => Y16ToRgb(width, height, data),
                    Format.OB_FORMAT_Z16 => Y16ToRgb(width, height, data),
                    Format.OB_FORMAT_RLE => Y16ToRgb(width, height, data),
                    Format.OB_FORMAT_RVL => Y16ToRgb(width, height, data),
                    Format.OB_FORMAT_GRAY => Y8ToRgb(width, height, data),
                    Format.OB_FORMAT_YV12 => Y8ToRgb(width, height, data),
                    Format.OB_FORMAT_BA81 => Y8ToRgb(width, height, data),

                    // YUV packed formats
                    Format.OB_FORMAT_YUY2 => Yuy2ToRgb(width, height, data),
                    Format.OB_FORMAT_YUYV => Yuy2ToRgb(width, height, data),

                    // Try generic conversion for other formats
                    Format.OB_FORMAT_BYR2 => Y16ToRgb(width, height, data),
                    Format.OB_FORMAT_RW16 => Y16ToRgb(width, height, data),
                    Format.OB_FORMAT_Y12C4 => Y16ToRgb(width, height, data),
                    _ => TryGenericConversion(width, height, data)
                };
            }
            catch
            {
                return null;
            }
        }
    }
}
