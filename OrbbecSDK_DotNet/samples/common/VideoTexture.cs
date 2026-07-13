using OpenTK.Graphics.OpenGL4;
using GLPixelType = OpenTK.Graphics.OpenGL4.PixelType;
using Orbbec;
using System.Collections.Concurrent;

namespace Samples.Common
{
    public class VideoTexture : IDisposable
    {
        public int TextureId { get; }
        public int Width { get; private set; }
        public int Height { get; private set; }

        private readonly bool _hasAlpha;
        private bool _isInitialized = false;
        private readonly Queue<TextureData> _updateQueue = new();
        private readonly object _queueLock = new();

        // Static Filter cache for format conversion
        private static readonly ConcurrentDictionary<ConvertFormat, FormatConvertFilter> _filterCache = new();

        public VideoTexture(bool hasAlpha = false)
        {
            _hasAlpha = hasAlpha;

            TextureId = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, TextureId);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);
        }

        /// <summary>
        /// Get or create format conversion Filter
        /// </summary>
        private static FormatConvertFilter GetOrCreateFilter(ConvertFormat convertFormat)
        {
            return _filterCache.GetOrAdd(convertFormat, format =>
            {
                var filter = new FormatConvertFilter();
                filter.SetConvertFormat(format);
                return filter;
            });
        }

        /// <summary>
        /// Convert frame format using SDK Filter
        /// </summary>
        private static byte[]? ConvertWithFilter(Frame frame, ConvertFormat convertFormat, out int outWidth, out int outHeight)
        {
            outWidth = 0;
            outHeight = 0;

            try
            {
                var filter = GetOrCreateFilter(convertFormat);
                using var convertedFrame = filter.Process(frame);
                if (convertedFrame == null)
                {
                    return null;
                }

                var streamProfile = convertedFrame.GetStreamProfile();
                using var videoProfile = streamProfile.As<VideoStreamProfile>();
                outWidth = (int)videoProfile.GetWidth();
                outHeight = (int)videoProfile.GetHeight();

                byte[] data = new byte[convertedFrame.GetDataSize()];
                convertedFrame.CopyData(ref data);
                return data;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Process frame and return converted RGB data.
        /// If originalFrame is provided and format requires filter conversion, SDK Filter will be used.
        /// </summary>
        public byte[]? ProcessFrame(int width, int height, Format format, byte[]? data, Frame? originalFrame, out int outWidth, out int outHeight)
        {
            outWidth = width;
            outHeight = height;

            // Get conversion info for this format
            var (conversionType, filterFormat) = ImageUtils.GetConversionInfo(format);

            // Handle not supported formats
            if (conversionType == ConversionType.NotSupported)
            {
                throw new NotSupportedException($"{format} format requires external decoding");
            }

            // Handle formats requiring SDK Filter
            if (conversionType == ConversionType.RequireFilter && originalFrame != null && filterFormat.HasValue)
            {
                return ConvertWithFilter(originalFrame, filterFormat.Value, out outWidth, out outHeight);
            }

            // For standard conversion, data must not be null
            if (data == null || data.Length == 0)
                return null;

            // Standard conversion
            return ImageUtils.ConvertFrameToRgb(width, height, format, data);
        }

        /// <summary>
        /// Queue a frame update. If originalFrame is provided and format requires filter conversion, SDK Filter will be used.
        /// </summary>
        public void UpdateQueue(int width, int height, Format format, byte[] data, Frame? originalFrame = null)
        {
            if (data == null || data.Length == 0)
            {
                return;
            }

            lock (_queueLock)
            {
                try
                {
                    var convertedData = ProcessFrame(width, height, format, data, originalFrame, out int finalWidth, out int finalHeight);

                    if (convertedData == null)
                    {
                        return;
                    }

                    var updateData = new TextureData
                    {
                        Width = finalWidth,
                        Height = finalHeight,
                        Data = convertedData
                    };

                    _updateQueue.Enqueue(updateData);

                    // Keep queue size limited to avoid memory issues
                    while (_updateQueue.Count > 3)
                    {
                        _updateQueue.Dequeue();
                    }
                }
                catch (Exception)
                {
                    // Silently ignore conversion errors
                }
            }
        }

        public void ProcessFrames()
        {
            lock (_queueLock)
            {
                if (_updateQueue.Count == 0)
                {
                    return;
                }

                TextureData latestUpdate = _updateQueue.Dequeue();

                while (_updateQueue.Count > 0)
                {
                    latestUpdate = _updateQueue.Dequeue();
                }

                UpdateTextureInternal(latestUpdate.Width, latestUpdate.Height, latestUpdate.Data);
            }
        }

        public void UpdateQueueDirect(int width, int height, byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return;
            }

            lock (_queueLock)
            {
                var updateData = new TextureData
                {
                    Width = width,
                    Height = height,
                    Data = data
                };

                _updateQueue.Enqueue(updateData);

                while (_updateQueue.Count > 3)
                {
                    _updateQueue.Dequeue();
                }
            }
        }

        private void UpdateTextureInternal(int width, int height, byte[] data)
        {
            GL.BindTexture(TextureTarget.Texture2D, TextureId);

            if (!_isInitialized || width != Width || height != Height)
            {
                Width = width;
                Height = height;

                var internalFormat = _hasAlpha ? PixelInternalFormat.Rgba : PixelInternalFormat.Rgb;
                var pixelFormat = _hasAlpha ? PixelFormat.Rgba : PixelFormat.Rgb;

                GL.TexImage2D(TextureTarget.Texture2D, 0,
                    internalFormat,
                    Width, Height, 0,
                    pixelFormat,
                    GLPixelType.UnsignedByte, data);

                _isInitialized = true;
            }
            else
            {
                var pixelFormat = _hasAlpha ? PixelFormat.Rgba : PixelFormat.Rgb;
                GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, Width, Height,
                    pixelFormat,
                    GLPixelType.UnsignedByte, data);
            }
        }

        public void Dispose()
        {
            if (TextureId != 0)
            {
                GL.DeleteTexture(TextureId);
                _isInitialized = false;
            }
        }

        private struct TextureData
        {
            public int Width { get; set; }
            public int Height { get; set; }
            public byte[] Data { get; set; }
        }
    }
}
