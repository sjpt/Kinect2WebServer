using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.Common;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.GraphicsLibraryFramework;
using GLErrorCode = OpenTK.Graphics.OpenGL4.ErrorCode;
using OpenTK.Mathematics;
using Orbbec;
using System.Diagnostics;
using SixLabors.ImageSharp;

namespace Samples.Common
{
    /// <summary>
    /// Frame timestamp information
    /// </summary>
    public class FrameTimestampInfo
    {
        /// <summary>Frame timestamp (microseconds)</summary>
        public ulong FrameTimestampUs { get; set; }
        /// <summary>System timestamp (microseconds)</summary>
        public ulong SystemTimestampUs { get; set; }
        /// <summary>Global timestamp (microseconds)</summary>
        public ulong GlobalTimestampUs { get; set; }
        /// <summary>Frame type description</summary>
        public string FrameType { get; set; } = "";
        /// <summary>Device index</summary>
        public int DeviceIndex { get; set; }
    }

    /// <summary>
    /// Text display configuration
    /// </summary>
    public class TextDisplayConfig
    {
        /// <summary>Text content</summary>
        public string Text { get; set; } = "";
        /// <summary>Position X (pixels)</summary>
        public float X { get; set; }
        /// <summary>Position Y (pixels)</summary>
        public float Y { get; set; }
        /// <summary>Text scale</summary>
        public float Scale { get; set; } = 1.0f;
        /// <summary>Text color</summary>
        public Color Color { get; set; } = Color.White;
        /// <summary>Whether to show the text</summary>
        public bool Visible { get; set; } = true;
        /// <summary>Font family name</summary>
        public string FontFamily { get; set; } = "Arial";
        /// <summary>Font size in pixels</summary>
        public float FontSize { get; set; } = 16f;
        /// <summary>Whether to use bold font</summary>
        public bool Bold { get; set; } = false;
        /// <summary>Whether to horizontally center the text</summary>
        public bool Centered { get; set; } = false;
        /// <summary>Outline color</summary>
        public Color OutlineColor { get; set; } = Color.Black;
        /// <summary>Outline thickness in pixels</summary>
        public float OutlineThickness { get; set; } = 0f;
    }

    public class OrbbecRenderer : GameWindow
    {
        private int _program;
        private int _vao;
        private int _vbo;
        private readonly List<VideoTexture> _videoTextures = new List<VideoTexture>{};
        private readonly object _textureLock = new();

        // Timestamp display related
        private readonly Dictionary<int, FrameTimestampInfo> _timestampInfos = new();
        private readonly object _timestampLock = new();
        private bool _showTimestamp = false; // Disabled by default, needs explicit enable

        // Text renderer for displaying custom text
        private TextRenderer? _textRenderer;
        private readonly Dictionary<string, TextDisplayConfig> _textDisplays = new();
        private readonly object _textDisplayLock = new();

        // Grid cell position information for text rendering (key: cell index, value: (x, y, width, height))
        private readonly Dictionary<int, (float x, float y, float width, float height)> _gridCellPositions = new();
        private readonly object _gridCellLock = new();

        // Show grid info text
        private bool _showGridInfo = false;
        private string _gridInfoFormat = "Cell {0}";

        // Key pressed event
        public event Action<Keys>? KeyPressed;

        private static readonly float[] _vertices = new float[]
        {
            -1f, 1f, 0f, 0f,
            -1f, -1f, 0f, 1f,
            1f, -1f, 1f, 1f,
            1f, 1f, 1f, 0f,
        };

        public OrbbecRenderer(int width = 1280, int height = 720, string title = "")
            : base(GameWindowSettings.Default,
                new NativeWindowSettings()
                {
                    ClientSize = (width, height),
                    Title = title
                })
        {
            VSync = VSyncMode.On;
        }

        /// <summary>
        /// Whether to display timestamp information
        /// </summary>
        public bool ShowTimestamp
        {
            get => _showTimestamp;
            set => _showTimestamp = value;
        }

        /// <summary>
        /// Update video frame timestamp information
        /// </summary>
        public void UpdateTimestampInfo(int textureIndex, FrameTimestampInfo info)
        {
            lock (_timestampLock)
            {
                _timestampInfos[textureIndex] = info;
            }
        }

        /// <summary>
        /// Get video frame timestamp information
        /// </summary>
        public FrameTimestampInfo? GetTimestampInfo(int textureIndex)
        {
            lock (_timestampLock)
            {
                return _timestampInfos.TryGetValue(textureIndex, out var info) ? info : null;
            }
        }

        public int AddVideoFrame(bool hasAlpha = false)
        {
            var tex = new VideoTexture(hasAlpha);
            lock (_textureLock)
            {
                _videoTextures.Add(tex);
                int index = _videoTextures.Count - 1;
                return index;
            }
        }

        public void UpdateVideoFrame(int index, int width, int height, Format format, byte[] data, Frame? originalFrame = null)
        {
            if (index < 0 || index >= _videoTextures.Count)
            {
                Console.WriteLine($"[OrbbecRenderer] Invalid texture index: {index}, available: {_videoTextures.Count}");
                return;
            }

            _videoTextures[index].UpdateQueue(width, height, format, data, originalFrame);
        }

        [Obsolete("Use UpdateVideoFrame instead. This method will be removed in future versions.")]
        public void UpdateVideoFrameWithFilter(int index, int width, int height, Format format, byte[] data, Frame originalFrame)
        {
            UpdateVideoFrame(index, width, height, format, data, originalFrame);
        }

        protected override void OnLoad()
        {
            base.OnLoad();
            GL.ClearColor(Color4.DarkGray);
            CheckGLError("Clear color setup");

            string vert = @"
                #version 330 core
                layout(location = 0) in vec2 aPos;
                layout(location = 1) in vec2 aTex;
                out vec2 TexCoord;
                void main() {
                    gl_Position = vec4(aPos, 0.0, 1.0);
                    TexCoord = aTex;
                }
                ";

            string frag = @"
                #version 330 core
                out vec4 FragColor;
                in vec2 TexCoord;
                uniform sampler2D tex;
                void main() {
                    FragColor = texture(tex, TexCoord);
                }
                ";

            _program = CreateShaderProgram(vert, frag);
            if (_program == 0) return;

            _vao = GL.GenVertexArray();
            CheckGLError("Generate VAO");
            if (_vao == 0)
            {
                Console.WriteLine("Failed to generate VAO");
                return;
            }
            GL.BindVertexArray(_vao);

            _vbo = GL.GenBuffer();
            CheckGLError("Generate VBO");
            if (_vbo == 0)
            {
                Console.WriteLine("Failed to generate VBO");
                return;
            }
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, _vertices.Length * sizeof(float), _vertices, BufferUsageHint.StaticDraw);

            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
            GL.EnableVertexAttribArray(1);

            GL.BindVertexArray(0);

            // Initialize text renderer
            _textRenderer = new TextRenderer("Arial", 16f);
            _textRenderer.SetWindowSize(ClientSize.X, ClientSize.Y);
        }

        protected override void OnUpdateFrame(FrameEventArgs e)
        {
            base.OnUpdateFrame(e);

            if (KeyboardState.IsKeyDown(Keys.Escape))
                Close();

            // Check for key presses and trigger event (only on key press, not hold)
            bool keyTriggered = false;
            if (KeyboardState.IsKeyPressed(Keys.S))
            {
                KeyPressed?.Invoke(Keys.S);
                keyTriggered = true;
            }
            if (KeyboardState.IsKeyPressed(Keys.T))
            {
                KeyPressed?.Invoke(Keys.T);
                keyTriggered = true;
            }

            // Add small delay after key press to simulate C++ waitKey behavior
            if (keyTriggered)
            {
                System.Threading.Thread.Sleep(100);
            }

            lock (_textureLock)
            {
                if (_videoTextures.Count > 0)
                {
                    foreach (var texture in _videoTextures)
                    {
                        texture.ProcessFrames();
                    }
                }
            }
        }

        protected override void OnRenderFrame(FrameEventArgs e)
        {
            base.OnRenderFrame(e);

            // Use ClientSize (actual drawable area) instead of Size (includes window borders)
            int clientWidth = ClientSize.X;
            int clientHeight = ClientSize.Y;

            // Ensure viewport is set to full client area before clearing
            GL.Viewport(0, 0, clientWidth, clientHeight);
            GL.Clear(ClearBufferMask.ColorBufferBit);
            CheckGLError("Clear buffer");

            lock (_textureLock)
            {
                if (_videoTextures.Count > 0)
                {
                    RenderVideoGrid(_videoTextures, clientWidth, clientHeight);
                }
            }

            // Render custom text displays
            RenderTextDisplays();

            SwapBuffers();
        }

        /// <summary>
        /// Render all text displays including grid info
        /// </summary>
        private void RenderTextDisplays()
        {
            if (_textRenderer == null) return;

            // Render timestamp text for each grid cell (if enabled)
            if (_showTimestamp)
            {
                RenderTimestampText();
            }

            // Render custom text displays
            lock (_textDisplayLock)
            {
                foreach (var kvp in _textDisplays)
                {
                    var config = kvp.Value;
                    if (!config.Visible) continue;

                    // Set font based on configuration
                    if (config.Bold)
                    {
                        _textRenderer.SetFont(config.FontFamily, config.FontSize, SixLabors.Fonts.FontStyle.Bold);
                    }
                    else
                    {
                        _textRenderer.SetFont(config.FontFamily, config.FontSize);
                    }

                    _textRenderer.UpdateText(config.Text);

                    // Calculate position (handle centering)
                    float x = config.X;
                    if (config.Centered)
                    {
                        var (textWidth, _) = _textRenderer.GetTextSize();
                        x = (ClientSize.X - textWidth * config.Scale) / 2f;
                    }

                    _textRenderer.SetPosition(x, config.Y);
                    _textRenderer.SetScale(config.Scale);
                    _textRenderer.SetColor(config.Color);
                    _textRenderer.SetOutline(config.OutlineColor, config.OutlineThickness);
                    _textRenderer.Render();
                }
            }

            // Render grid cell info text
            if (_showGridInfo)
            {
                RenderGridInfoText();
            }
        }

        /// <summary>
        /// Render text at the top-left corner of each grid cell
        /// </summary>
        private void RenderGridInfoText()
        {
            if (_textRenderer == null) return;

            lock (_gridCellLock)
            {
                foreach (var kvp in _gridCellPositions)
                {
                    int cellIndex = kvp.Key;
                    var pos = kvp.Value;

                    string text = string.Format(_gridInfoFormat, cellIndex);

                    // Render text at the top-left corner of the cell with a small margin
                    float margin = 5f;
                    _textRenderer.UpdateText(text);
                    _textRenderer.SetPosition(pos.x + margin, pos.y + margin);
                    _textRenderer.SetScale(1.0f);
                    _textRenderer.SetColor(Color.White);
                    _textRenderer.Render();
                }
            }
        }

        /// <summary>
        /// Render timestamp text at the top-left corner of each grid cell
        /// </summary>
        private void RenderTimestampText()
        {
            if (_textRenderer == null) return;

            // Set larger font size for timestamp display (regular style, not bold)
            _textRenderer.SetFont("Arial", 24f);

            lock (_gridCellLock)
            {
                lock (_timestampLock)
                {
                    foreach (var kvp in _gridCellPositions)
                    {
                        int cellIndex = kvp.Key;
                        var pos = kvp.Value;

                        // Check if we have timestamp info for this cell
                        if (_timestampInfos.TryGetValue(cellIndex, out var timestampInfo))
                        {
                            // Render text at the top-left corner of the cell with a small margin
                            float margin = 8f;
                            float lineHeight = 26f;

                            // Use white background with black text for better readability
                            _textRenderer.SetBackgroundColor(Color.White);

                            // Line 1: Device and Frame Type
                            _textRenderer.UpdateText($"Dev{timestampInfo.DeviceIndex} {timestampInfo.FrameType}");
                            _textRenderer.SetPosition(pos.x + margin, pos.y + margin);
                            _textRenderer.SetScale(1.0f);
                            _textRenderer.SetColor(Color.Black);
                            _textRenderer.Render();

                            // Line 2: Frame timestamp (us)
                            _textRenderer.UpdateText($"frame timestamp(us): {timestampInfo.FrameTimestampUs}");
                            _textRenderer.SetPosition(pos.x + margin, pos.y + margin + lineHeight);
                            _textRenderer.SetColor(Color.Black);
                            _textRenderer.Render();

                            // Line 3: System timestamp (us)
                            _textRenderer.UpdateText($"system timestamp(us): {timestampInfo.SystemTimestampUs}");
                            _textRenderer.SetPosition(pos.x + margin, pos.y + margin + lineHeight * 2);
                            _textRenderer.SetColor(Color.Black);
                            _textRenderer.Render();

                            // Reset background color
                            _textRenderer.SetBackgroundColor(Color.Transparent);
                        }
                    }
                }
            }

            // Reset font size to default
            _textRenderer.SetFont("Arial", 16f);
        }

        /// <summary>
        /// Format timestamp info into a display string
        /// </summary>
        private string FormatTimestampInfo(FrameTimestampInfo info)
        {
            return $"Dev{info.DeviceIndex} {info.FrameType}\n" +
                   $"Frame: {info.FrameTimestampUs / 1000}ms\n" +
                   $"Sys: {info.SystemTimestampUs / 1000}ms";
        }

        /// <summary>
        /// Add or update a text display with the given ID
        /// </summary>
        /// <param name="id">Unique identifier for this text display</param>
        /// <param name="text">Text content</param>
        /// <param name="x">X position in pixels (0 = left)</param>
        /// <param name="y">Y position in pixels (0 = top)</param>
        public void SetTextDisplay(string id, string text, float x, float y)
        {
            lock (_textDisplayLock)
            {
                if (_textDisplays.TryGetValue(id, out var config))
                {
                    config.Text = text;
                    config.X = x;
                    config.Y = y;
                }
                else
                {
                    _textDisplays[id] = new TextDisplayConfig
                    {
                        Text = text,
                        X = x,
                        Y = y,
                        Visible = true
                    };
                }
            }
        }

        /// <summary>
        /// Update only the text content for an existing display
        /// </summary>
        /// <param name="id">Text display ID</param>
        /// <param name="text">New text content</param>
        public void UpdateTextContent(string id, string text)
        {
            lock (_textDisplayLock)
            {
                if (_textDisplays.TryGetValue(id, out var config))
                {
                    config.Text = text;
                }
            }
        }

        /// <summary>
        /// Update only the position for an existing text display
        /// </summary>
        /// <param name="id">Text display ID</param>
        /// <param name="x">New X position in pixels</param>
        /// <param name="y">New Y position in pixels</param>
        public void UpdateTextPosition(string id, float x, float y)
        {
            lock (_textDisplayLock)
            {
                if (_textDisplays.TryGetValue(id, out var config))
                {
                    config.X = x;
                    config.Y = y;
                }
            }
        }

        /// <summary>
        /// Set the scale for a text display
        /// </summary>
        /// <param name="id">Text display ID</param>
        /// <param name="scale">Scale factor (1.0 = original size)</param>
        public void SetTextScale(string id, float scale)
        {
            lock (_textDisplayLock)
            {
                if (_textDisplays.TryGetValue(id, out var config))
                {
                    config.Scale = scale;
                }
            }
        }

        /// <summary>
        /// Set the color for a text display
        /// </summary>
        /// <param name="id">Text display ID</param>
        /// <param name="color">Text color</param>
        public void SetTextColor(string id, Color color)
        {
            lock (_textDisplayLock)
            {
                if (_textDisplays.TryGetValue(id, out var config))
                {
                    config.Color = color;
                }
            }
        }

        /// <summary>
        /// Set the font for a text display
        /// </summary>
        /// <param name="id">Text display ID</param>
        /// <param name="fontFamily">Font family name</param>
        /// <param name="fontSize">Font size in pixels</param>
        public void SetTextFont(string id, string fontFamily, float fontSize)
        {
            lock (_textDisplayLock)
            {
                if (_textDisplays.TryGetValue(id, out var config))
                {
                    config.FontFamily = fontFamily;
                    config.FontSize = fontSize;
                }
            }
        }

        /// <summary>
        /// Set whether the text display uses bold font
        /// </summary>
        /// <param name="id">Text display ID</param>
        /// <param name="bold">Whether to use bold font</param>
        public void SetTextBold(string id, bool bold)
        {
            lock (_textDisplayLock)
            {
                if (_textDisplays.TryGetValue(id, out var config))
                {
                    config.Bold = bold;
                }
            }
        }

        /// <summary>
        /// Set whether the text display is horizontally centered
        /// </summary>
        /// <param name="id">Text display ID</param>
        /// <param name="centered">Whether to center the text horizontally</param>
        public void SetTextCentered(string id, bool centered)
        {
            lock (_textDisplayLock)
            {
                if (_textDisplays.TryGetValue(id, out var config))
                {
                    config.Centered = centered;
                }
            }
        }

        /// <summary>
        /// Set the outline for a text display
        /// </summary>
        /// <param name="id">Text display ID</param>
        /// <param name="color">Outline color</param>
        /// <param name="thickness">Outline thickness in pixels</param>
        public void SetTextOutline(string id, Color color, float thickness = 2f)
        {
            lock (_textDisplayLock)
            {
                if (_textDisplays.TryGetValue(id, out var config))
                {
                    config.OutlineColor = color;
                    config.OutlineThickness = thickness;
                }
            }
        }

        /// <summary>
        /// Show or hide a text display
        /// </summary>
        /// <param name="id">Text display ID</param>
        /// <param name="visible">Whether to show the text</param>
        public void SetTextVisible(string id, bool visible)
        {
            lock (_textDisplayLock)
            {
                if (_textDisplays.TryGetValue(id, out var config))
                {
                    config.Visible = visible;
                }
            }
        }

        /// <summary>
        /// Remove a text display
        /// </summary>
        /// <param name="id">Text display ID</param>
        public void RemoveTextDisplay(string id)
        {
            lock (_textDisplayLock)
            {
                _textDisplays.Remove(id);
            }
        }

        /// <summary>
        /// Clear all text displays
        /// </summary>
        public void ClearTextDisplays()
        {
            lock (_textDisplayLock)
            {
                _textDisplays.Clear();
            }
        }

        protected override void OnResize(ResizeEventArgs e)
        {
            base.OnResize(e);
            // Use ClientSize for actual drawable area
            GL.Viewport(0, 0, ClientSize.X, ClientSize.Y);

            // Update text renderer window size
            _textRenderer?.SetWindowSize(ClientSize.X, ClientSize.Y);
        }

        protected override void OnUnload()
        {
            lock (_textureLock)
            {
                foreach (var texture in _videoTextures)
                {
                    texture.Dispose();
                }
                _videoTextures.Clear();
            }

            // Cleanup text renderer
            _textRenderer?.Dispose();
            _textRenderer = null;

            if (_program != 0)
            {
                GL.DeleteProgram(_program);
                _program = 0;
            }

            if (_vao != 0)
            {
                GL.DeleteVertexArray(_vao);
                _vao = 0;
            }

            if (_vbo != 0)
            {
                GL.DeleteBuffer(_vbo);
                _vbo = 0;
            }
            base.OnUnload();
        }

        private int CreateShaderProgram(string vertSrc, string fragSrc)
        {
            int vert = GL.CreateShader(ShaderType.VertexShader);
            CheckGLError("Create vertex shader");
            if (vert == 0)
            {
                Console.WriteLine("Failed to create vertex shader");
                return 0;
            }
            GL.ShaderSource(vert, vertSrc);
            GL.CompileShader(vert);

            int frag = GL.CreateShader(ShaderType.FragmentShader);
            CheckGLError("Create fragment shader");
            if (frag == 0)
            {
                Console.WriteLine("Failed to create fragment shader");
                GL.DeleteShader(vert);
                return 0;
            }
            GL.ShaderSource(frag, fragSrc);
            GL.CompileShader(frag);

            int prog = GL.CreateProgram();
            CheckGLError("Create shader program");
            if (prog == 0)
            {
                Console.WriteLine("Failed to create shader program");
                GL.DeleteShader(vert);
                GL.DeleteShader(frag);
                return 0;
            }
            GL.AttachShader(prog, vert);
            GL.AttachShader(prog, frag);
            GL.LinkProgram(prog);

            GL.DeleteShader(vert);
            GL.DeleteShader(frag);
            CheckGLError("Cleanup shaders");

            return prog;
        }

        /// <summary>
        /// Get the position of a specific grid cell
        /// </summary>
        /// <param name="cellIndex">Cell index (0-based)</param>
        /// <returns>Cell position (x, y, width, height) or null if not found</returns>
        public (float x, float y, float width, float height)? GetGridCellPosition(int cellIndex)
        {
            lock (_gridCellLock)
            {
                return _gridCellPositions.TryGetValue(cellIndex, out var pos) ? pos : null;
            }
        }

        /// <summary>
        /// Show/hide grid cell info text
        /// </summary>
        public bool ShowGridInfo
        {
            get => _showGridInfo;
            set => _showGridInfo = value;
        }

        /// <summary>
        /// Set the format string for grid info text (use {0} for cell index)
        /// </summary>
        public string GridInfoFormat
        {
            get => _gridInfoFormat;
            set => _gridInfoFormat = value;
        }

        private void RenderVideoGrid(List<VideoTexture> textures, int winWidth, int winHeight)
        {
            if (textures.Count == 0)
            {
                return;
            }

            // Save current viewport to restore later
            int[] originalViewport = new int[4];
            GL.GetInteger(GetPName.Viewport, originalViewport);

            GL.UseProgram(_program);
            GL.BindVertexArray(_vao);

            // Clear previous cell positions
            lock (_gridCellLock)
            {
                _gridCellPositions.Clear();
            }

            try
            {
                int textureCount = textures.Count;
                int cols = (int)Math.Ceiling(Math.Sqrt(textureCount));
                int rows = (int)Math.Ceiling(textureCount / (float)cols);

                float cellWidth = winWidth / (float)cols;
                float cellHeight = winHeight / (float)rows;

                int renderedCount = 0;
                for (int i = 0; i < textureCount; ++i)
                {
                    var tex = textures[i];
                    if (tex.Width <= 0 || tex.Height <= 0)
                    {
                        continue;
                    }

                    int videoWidth = tex.Width;
                    int videoHeight = tex.Height;

                    float scale = Math.Min(cellWidth / videoWidth, cellHeight / videoHeight);
                    float displayW = videoWidth * scale;
                    float displayH = videoHeight * scale;

                    int col = i % cols;
                    int row = i / cols;

                    // Calculate the top-left corner of the video display area (screen coordinates)
                    // Screen coordinates: origin at top-left, Y increases downward
                    float videoTopLeftX = col * cellWidth + (cellWidth - displayW) / 2f;
                    float videoTopLeftY = row * cellHeight + (cellHeight - displayH) / 2f;

                    // Store cell position for text rendering (screen coordinates)
                    lock (_gridCellLock)
                    {
                        _gridCellPositions[i] = (videoTopLeftX, videoTopLeftY, displayW, displayH);
                    }

                    // OpenGL viewport uses bottom-left origin, so convert Y
                    float offsetX = videoTopLeftX;
                    float offsetY = winHeight - videoTopLeftY - displayH;

                    // Validate viewport coordinates
                    if (offsetX < 0) offsetX = 0;
                    if (offsetY < 0) offsetY = 0;
                    if (offsetX + displayW > winWidth) displayW = winWidth - offsetX;
                    if (offsetY + displayH > winHeight) displayH = winHeight - offsetY;

                    GL.Viewport((int)offsetX, (int)offsetY, (int)displayW, (int)displayH);

                    GL.ActiveTexture(TextureUnit.Texture0);
                    GL.BindTexture(TextureTarget.Texture2D, tex.TextureId);
                    GL.Uniform1(GL.GetUniformLocation(_program, "tex"), 0);

                    GL.DrawArrays(PrimitiveType.TriangleFan, 0, 4);
                    CheckGLError($"Draw call for texture {i}");
                    renderedCount++;
                }

                // Ensure all video grid drawing commands complete before proceeding
                GL.Finish();
            }
            finally
            {
                GL.BindVertexArray(0);
                // Restore original viewport
                GL.Viewport(originalViewport[0], originalViewport[1], originalViewport[2], originalViewport[3]);
            }
        }

        [Conditional("DEBUG")]
        private void CheckGLError(string context)
        {
            GLErrorCode error = GL.GetError();
            if (error != GLErrorCode.NoError)
            {
                Console.WriteLine($"OpenGL Error in {context}: {error}");
            }
        }
    }
}
