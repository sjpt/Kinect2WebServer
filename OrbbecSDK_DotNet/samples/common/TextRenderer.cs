using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;

namespace Samples.Common
{
    /// <summary>
    /// OpenGL text renderer - supports updating text content and position independently
    /// Cross-platform implementation using SixLabors.ImageSharp
    /// </summary>
    public class TextRenderer : IDisposable
    {
        private int _textProgram;
        private int _vao;
        private int _vbo;
        private int _textureId;

        private string _currentText = "";
        private int _textWidth;
        private int _textHeight;
        private float _posX = 0;
        private float _posY = 0;
        private float _scale = 1.0f;

        // Font settings
        private Font _font;
        private Color _textColor = Color.White;
        private Color _bgColor = Color.Transparent;

        // Outline settings
        private Color _outlineColor = Color.Black;
        private float _outlineThickness = 0f;

        // Window size for coordinate conversion
        private int _windowWidth = 1280;
        private int _windowHeight = 720;

        // Font collection for loading system fonts
        private static readonly FontCollection _fontCollection = new();
        private static readonly FontFamily _defaultFontFamily;

        // Shader source code
        private static readonly string TextVertShader = @"
            #version 330 core
            layout(location = 0) in vec2 aPos;
            layout(location = 1) in vec2 aTex;
            out vec2 TexCoord;
            uniform vec2 uPosition;
            uniform vec2 uScale;
            uniform vec2 uScreenSize;
            void main() {
                vec2 screenPos = aPos * uScale + uPosition;
                vec2 ndc = (screenPos / uScreenSize) * 2.0 - 1.0;
                gl_Position = vec4(ndc.x, -ndc.y, 0.0, 1.0);
                TexCoord = aTex;
            }
            ";

        private static readonly string TextFragShader = @"
            #version 330 core
            out vec4 FragColor;
            in vec2 TexCoord;
            uniform sampler2D tex;
            uniform vec4 uTextColor;
            uniform vec4 uBgColor;
            void main() {
                vec4 sampled = texture(tex, TexCoord);
                if (sampled.a < 0.01) {
                    if (uBgColor.a > 0.01) {
                        FragColor = uBgColor;
                    } else {
                        discard;
                    }
                } else {
                    FragColor = vec4(uTextColor.rgb, sampled.a * uTextColor.a);
                }
            }
            ";

        // Vertex data for a quad
        private static readonly float[] QuadVertices = new float[]
        {
            0f,  0f,   0f, 0f,
            0f,  1f,   0f, 1f,
            1f,  1f,   1f, 1f,
            1f,  0f,   1f, 0f,
        };

        static TextRenderer()
        {
            // Try to load system fonts
            try
            {
                _fontCollection.AddSystemFonts();
            }
            catch
            {
                // Ignore system font loading errors
            }

            // Default to a common font or fallback
            if (_fontCollection.TryGet("Arial", out var arial))
                _defaultFontFamily = arial;
            else if (_fontCollection.TryGet("DejaVu Sans", out var dejavu))
                _defaultFontFamily = dejavu;
            else if (_fontCollection.TryGet("Microsoft YaHei", out var yahei))
                _defaultFontFamily = yahei;
            else if (_fontCollection.Families.Any())
                _defaultFontFamily = _fontCollection.Families.First();
            else
                _defaultFontFamily = default; // Will throw if used
        }

        /// <summary>
        /// Create a new text renderer
        /// </summary>
        public TextRenderer(string fontFamily = "Arial", float fontSize = 16f)
        {
            var family = GetFontFamily(fontFamily);
            _font = family.CreateFont(fontSize, FontStyle.Regular);
            InitializeOpenGL();
        }

        /// <summary>
        /// Create a new text renderer with custom font
        /// </summary>
        public TextRenderer(Font font)
        {
            _font = font;
            InitializeOpenGL();
        }

        private FontFamily GetFontFamily(string name)
        {
            if (_fontCollection.TryGet(name, out var family))
                return family;

            if (_defaultFontFamily != default)
                return _defaultFontFamily;

            throw new InvalidOperationException($"Font '{name}' not found and no fallback available");
        }

        private void InitializeOpenGL()
        {
            _textProgram = CreateShaderProgram(TextVertShader, TextFragShader);

            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();

            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, QuadVertices.Length * sizeof(float), QuadVertices, BufferUsageHint.StaticDraw);

            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);

            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));

            GL.BindVertexArray(0);

            _textureId = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, _textureId);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        }

        /// <summary>
        /// Update the text content and regenerate the texture
        /// </summary>
        public void UpdateText(string text)
        {
            if (text == null) text = "";
            if (text == _currentText && _textWidth > 0) return;

            _currentText = text;

            if (string.IsNullOrEmpty(text))
            {
                _textWidth = 0;
                _textHeight = 0;
                return;
            }

            using (var image = RenderTextToImage(text))
            {
                _textWidth = image.Width;
                _textHeight = image.Height;
                UploadImageToTexture(image);
            }
        }

        /// <summary>
        /// Set the position of the text (in screen pixels, top-left origin)
        /// </summary>
        public void SetPosition(float x, float y)
        {
            _posX = x;
            _posY = y;
        }

        /// <summary>
        /// Set the position using normalized coordinates (0-1)
        /// </summary>
        public void SetPositionNormalized(float normalizedX, float normalizedY)
        {
            _posX = normalizedX * _windowWidth;
            _posY = normalizedY * _windowHeight;
        }

        /// <summary>
        /// Set the scale of the text
        /// </summary>
        public void SetScale(float scale)
        {
            _scale = scale;
        }

        /// <summary>
        /// Set the text color
        /// </summary>
        public void SetColor(Color color)
        {
            _textColor = color;
        }

        /// <summary>
        /// Set the background color
        /// </summary>
        public void SetBackgroundColor(Color color)
        {
            _bgColor = color;
        }

        /// <summary>
        /// Set the outline color and thickness
        /// </summary>
        /// <param name="color">Outline color</param>
        /// <param name="thickness">Outline thickness in pixels</param>
        public void SetOutline(Color color, float thickness = 2f)
        {
            _outlineColor = color;
            _outlineThickness = thickness;
        }

        /// <summary>
        /// Update window size for proper coordinate calculations
        /// </summary>
        public void SetWindowSize(int width, int height)
        {
            _windowWidth = width;
            _windowHeight = height;
        }

        /// <summary>
        /// Change the font
        /// </summary>
        public void SetFont(Font font)
        {
            _font = font;
            if (!string.IsNullOrEmpty(_currentText))
            {
                var currentText = _currentText;
                _currentText = "";
                UpdateText(currentText);
            }
        }

        /// <summary>
        /// Change the font by family name and size
        /// </summary>
        public void SetFont(string fontFamily, float fontSize)
        {
            var family = GetFontFamily(fontFamily);
            _font = family.CreateFont(fontSize, FontStyle.Regular);
            if (!string.IsNullOrEmpty(_currentText))
            {
                var currentText = _currentText;
                _currentText = "";
                UpdateText(currentText);
            }
        }

        /// <summary>
        /// Change the font by family name, size and style
        /// </summary>
        public void SetFont(string fontFamily, float fontSize, FontStyle style)
        {
            var family = GetFontFamily(fontFamily);
            _font = family.CreateFont(fontSize, style);
            if (!string.IsNullOrEmpty(_currentText))
            {
                var currentText = _currentText;
                _currentText = "";
                UpdateText(currentText);
            }
        }

        /// <summary>
        /// Get the size of the current text
        /// </summary>
        public (int Width, int Height) GetTextSize()
        {
            return (_textWidth, _textHeight);
        }

        /// <summary>
        /// Render the text
        /// </summary>
        public void Render()
        {
            if (string.IsNullOrEmpty(_currentText) || _textWidth == 0 || _textHeight == 0)
                return;

            int[] oldViewport = new int[4];
            GL.GetInteger(GetPName.Viewport, oldViewport);
            GL.Viewport(0, 0, _windowWidth, _windowHeight);

            GL.UseProgram(_textProgram);
            GL.BindVertexArray(_vao);
            GL.BindTexture(TextureTarget.Texture2D, _textureId);

            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            var posLoc = GL.GetUniformLocation(_textProgram, "uPosition");
            var scaleLoc = GL.GetUniformLocation(_textProgram, "uScale");
            var screenSizeLoc = GL.GetUniformLocation(_textProgram, "uScreenSize");
            var textColorLoc = GL.GetUniformLocation(_textProgram, "uTextColor");
            var bgColorLoc = GL.GetUniformLocation(_textProgram, "uBgColor");

            GL.Uniform2(screenSizeLoc, (float)_windowWidth, (float)_windowHeight);
            GL.Uniform2(scaleLoc, _textWidth * _scale, _textHeight * _scale);

            // Render outline first (if enabled)
            if (_outlineThickness > 0)
            {
                var outlinePixel = _outlineColor.ToPixel<Rgba32>();
                GL.Uniform4(textColorLoc,
                    outlinePixel.R / 255f,
                    outlinePixel.G / 255f,
                    outlinePixel.B / 255f,
                    outlinePixel.A / 255f);
                GL.Uniform4(bgColorLoc, 0, 0, 0, 0); // No background for outline

                // Draw outline in 8 directions
                float[] offsets = {
                    -1, -1,  0, -1,  1, -1,
                    -1,  0,           1,  0,
                    -1,  1,  0,  1,  1,  1
                };

                for (int i = 0; i < offsets.Length; i += 2)
                {
                    float ox = offsets[i] * _outlineThickness;
                    float oy = offsets[i + 1] * _outlineThickness;
                    GL.Uniform2(posLoc, _posX + ox, _posY + oy);
                    GL.DrawArrays(PrimitiveType.TriangleFan, 0, 4);
                }
            }

            // Render main text
            GL.Uniform2(posLoc, _posX, _posY);
            var textPixel = _textColor.ToPixel<Rgba32>();
            var bgPixel = _bgColor.ToPixel<Rgba32>();
            GL.Uniform4(textColorLoc,
                textPixel.R / 255f,
                textPixel.G / 255f,
                textPixel.B / 255f,
                textPixel.A / 255f);
            GL.Uniform4(bgColorLoc,
                bgPixel.R / 255f,
                bgPixel.G / 255f,
                bgPixel.B / 255f,
                bgPixel.A / 255f);

            GL.DrawArrays(PrimitiveType.TriangleFan, 0, 4);

            GL.BindVertexArray(0);
            GL.UseProgram(0);
            GL.Disable(EnableCap.Blend);

            GL.Viewport(oldViewport[0], oldViewport[1], oldViewport[2], oldViewport[3]);
        }

        /// <summary>
        /// Render text at a specific position
        /// </summary>
        public void RenderAt(float x, float y)
        {
            if (string.IsNullOrEmpty(_currentText) || _textWidth == 0 || _textHeight == 0)
                return;

            int[] oldViewport = new int[4];
            GL.GetInteger(GetPName.Viewport, oldViewport);
            GL.Viewport(0, 0, _windowWidth, _windowHeight);

            GL.UseProgram(_textProgram);
            GL.BindVertexArray(_vao);
            GL.BindTexture(TextureTarget.Texture2D, _textureId);

            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            var posLoc = GL.GetUniformLocation(_textProgram, "uPosition");
            var scaleLoc = GL.GetUniformLocation(_textProgram, "uScale");
            var screenSizeLoc = GL.GetUniformLocation(_textProgram, "uScreenSize");
            var textColorLoc = GL.GetUniformLocation(_textProgram, "uTextColor");

            GL.Uniform2(posLoc, x, y);
            GL.Uniform2(scaleLoc, _textWidth * _scale, _textHeight * _scale);
            GL.Uniform2(screenSizeLoc, (float)_windowWidth, (float)_windowHeight);
            var textPixel = _textColor.ToPixel<Rgba32>();
            GL.Uniform4(textColorLoc,
                textPixel.R / 255f,
                textPixel.G / 255f,
                textPixel.B / 255f,
                textPixel.A / 255f);

            GL.DrawArrays(PrimitiveType.TriangleFan, 0, 4);

            GL.BindVertexArray(0);
            GL.UseProgram(0);
            GL.Disable(EnableCap.Blend);

            GL.Viewport(oldViewport[0], oldViewport[1], oldViewport[2], oldViewport[3]);
        }

        private Image<Rgba32> RenderTextToImage(string text)
        {
            // Measure text size
            var textOptions = new TextOptions(_font);
            var textSize = TextMeasurer.MeasureSize(text, textOptions);

            int width = (int)Math.Ceiling(textSize.Width) + 4;
            int height = (int)Math.Ceiling(textSize.Height) + 2;

            // Create image with transparent background
            var image = new Image<Rgba32>(width, height);

            // Draw text
            image.Mutate(ctx =>
            {
                ctx.DrawText(text, _font, _textColor, new PointF(2, 0));
            });

            return image;
        }

        private void UploadImageToTexture(Image<Rgba32> image)
        {
            GL.BindTexture(TextureTarget.Texture2D, _textureId);

            // Get image data as byte array
            byte[] pixelData = new byte[image.Width * image.Height * 4];
            image.CopyPixelDataTo(pixelData);

            GL.TexImage2D(
                TextureTarget.Texture2D,
                0,
                PixelInternalFormat.Rgba,
                image.Width,
                image.Height,
                0,
                OpenTK.Graphics.OpenGL4.PixelFormat.Rgba,
                PixelType.UnsignedByte,
                pixelData);

            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        private int CreateShaderProgram(string vertSrc, string fragSrc)
        {
            int vert = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vert, vertSrc);
            GL.CompileShader(vert);

            int frag = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(frag, fragSrc);
            GL.CompileShader(frag);

            int prog = GL.CreateProgram();
            GL.AttachShader(prog, vert);
            GL.AttachShader(prog, frag);
            GL.LinkProgram(prog);

            GL.DeleteShader(vert);
            GL.DeleteShader(frag);

            return prog;
        }

        public void Dispose()
        {
            if (_textureId != 0)
            {
                GL.DeleteTexture(_textureId);
                _textureId = 0;
            }

            if (_vbo != 0)
            {
                GL.DeleteBuffer(_vbo);
                _vbo = 0;
            }

            if (_vao != 0)
            {
                GL.DeleteVertexArray(_vao);
                _vao = 0;
            }

            if (_textProgram != 0)
            {
                GL.DeleteProgram(_textProgram);
                _textProgram = 0;
            }
        }
    }
}
