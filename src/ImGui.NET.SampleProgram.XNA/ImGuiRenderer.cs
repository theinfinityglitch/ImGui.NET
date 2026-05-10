using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace ImGuiNET.SampleProgram.XNA;

public class TextureInfo
{
    public Texture2D Texture { get; set; }
    public bool IsManaged { get; set; }
}

public class ImGuiRenderer
{
    private const float WHEEL_DELTA = 120;

    private readonly Game _game;

    // Graphics
    private readonly GraphicsDevice _graphicsDevice;
    private BasicEffect _effect;
    private readonly RasterizerState _rasterizerState;

    private byte[] _vertexData;
    private VertexBuffer _vertexBuffer;
    private int _vertexBufferSize;

    private byte[] _indexData;
    private IndexBuffer _indexBuffer;
    private int _indexBufferSize;

    // Textures
    private readonly Dictionary<ulong, TextureInfo> _textures = new();
    private ulong _nextTexId = 1;

    // Input
    private int _scrollWheelValue;
    private int _horizontalScrollWheelValue;
    private readonly Keys[] _allKeys = Enum.GetValues<Keys>();

    public ImGuiRenderer(Game game)
    {
        ArgumentNullException.ThrowIfNull(game);

        var context = ImGui.CreateContext();
        ImGui.SetCurrentContext(context);

        _game = game;
        _graphicsDevice = game.GraphicsDevice;

        _rasterizerState = new RasterizerState
        {
            CullMode = CullMode.None,
            DepthBias = 0,
            FillMode = FillMode.Solid,
            MultiSampleAntiAlias = false,
            ScissorTestEnable = true,
            SlopeScaleDepthBias = 0
        };

        SetupInput();
        SetupBackendCapabilities();
    }

    private void SetupBackendCapabilities()
    {
        ImGui.GetIO().BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
    }

    // Bind an external texture to ImGui (for user-created textures)
    public virtual ImTextureRef BindTexture(Texture2D texture)
    {
        var id = _nextTexId++;
        _textures[id] = new TextureInfo { Texture = texture, IsManaged = false };

        ImTextureRef ret = new()
        {
            _TexData = null,
            _TexID = id
        };

        return ret;
    }

    public virtual void UnbindTexture(ulong textureId)
    {
        if (_textures.TryGetValue(textureId, out var info))
        {
            if (info.IsManaged) info.Texture?.Dispose();
            _textures.Remove(textureId);
        }
    }

    // --- Texture lifecycle via ImTextureStatus ---

    public virtual void UpdateTexture(ImTextureDataPtr tex)
    {
        switch (tex.Status)
        {
            case ImTextureStatus.WantCreate:
                CreateTexture(tex);
                break;
            case ImTextureStatus.WantUpdates:
                UpdateTextureData(tex);
                break;
            case ImTextureStatus.WantDestroy:
                DestroyTexture(tex);
                break;
        }
    }

    private void CreateTexture(ImTextureDataPtr tex)
    {
        var format = tex.Format == ImTextureFormat.Alpha8
            ? SurfaceFormat.Alpha8
            : SurfaceFormat.Color; // RGBA32

        var texture = new Texture2D(_graphicsDevice, tex.Width, tex.Height, false, format);

        int bytesPerPixel = tex.Format == ImTextureFormat.Alpha8 ? 1 : 4;
        var pixels = new byte[tex.Width * tex.Height * bytesPerPixel];
        Marshal.Copy(tex.GetPixels(), pixels, 0, pixels.Length);
        texture.SetData(pixels);

        var id = _nextTexId++;
        _textures[id] = new TextureInfo { Texture = texture, IsManaged = true };
        tex.SetTexID(id);
        tex.Status = ImTextureStatus.OK;
    }

    private void UpdateTextureData(ImTextureDataPtr tex)
    {
        ulong texId = tex.GetTexID();
        if (!_textures.TryGetValue(texId, out var info)) return;

        int bytesPerPixel = tex.Format == ImTextureFormat.Alpha8 ? 1 : 4;
        var pixels = new byte[tex.Width * tex.Height * bytesPerPixel];
        Marshal.Copy(tex.GetPixels(), pixels, 0, pixels.Length);
        info.Texture.SetData(pixels);

        tex.Status = ImTextureStatus.OK;
    }

    private void DestroyTexture(ImTextureDataPtr tex)
    {
        ulong texId = tex.GetTexID();
        if (_textures.TryGetValue(texId, out var info))
        {
            if (info.IsManaged) info.Texture?.Dispose();
            _textures.Remove(texId);
        }
        tex.Status = ImTextureStatus.Destroyed;
    }

    // --- Frame ---

    public virtual void BeforeLayout(GameTime gameTime)
    {
        ImGui.GetIO().DeltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
        UpdateInput();
        ImGui.NewFrame();
    }

    public virtual void AfterLayout()
    {
        ImGui.Render();

        unsafe
        {
            ImDrawDataPtr drawData = ImGui.GetDrawData();
            ProcessTextureUpdates(drawData);
            RenderDrawData(drawData);
        }
    }

    private void ProcessTextureUpdates(ImDrawDataPtr drawData)
    {
        var textures = drawData.Textures;
        for (int i = 0; i < textures.Size; i++)
            UpdateTexture(textures[i]);
    }

    // --- Input ---

    protected virtual void SetupInput()
    {
        ImGuiIOPtr io = ImGui.GetIO();

        // MonoGame specific
        _game.Window.TextInput += (s, a) =>
        {
            if (a.Character == '\t') return;
            io.AddInputCharacter(a.Character);
        };
    }

    protected virtual void UpdateInput()
    {
        if (!_game.IsActive) return;

        ImGuiIOPtr io = ImGui.GetIO();
        MouseState mouse = Mouse.GetState();
        KeyboardState keyboard = Keyboard.GetState();

        io.AddMousePosEvent(mouse.X, mouse.Y);
        io.AddMouseButtonEvent(0, mouse.LeftButton == ButtonState.Pressed);
        io.AddMouseButtonEvent(1, mouse.RightButton == ButtonState.Pressed);
        io.AddMouseButtonEvent(2, mouse.MiddleButton == ButtonState.Pressed);
        io.AddMouseButtonEvent(3, mouse.XButton1 == ButtonState.Pressed);
        io.AddMouseButtonEvent(4, mouse.XButton2 == ButtonState.Pressed);

        io.AddMouseWheelEvent(
            (mouse.HorizontalScrollWheelValue - _horizontalScrollWheelValue) / WHEEL_DELTA,
            (mouse.ScrollWheelValue - _scrollWheelValue) / WHEEL_DELTA);

        _scrollWheelValue = mouse.ScrollWheelValue;
        _horizontalScrollWheelValue = mouse.HorizontalScrollWheelValue;

        foreach (var key in _allKeys)
        {
            if (TryMapKeys(key, out ImGuiKey imguiKey))
                io.AddKeyEvent(imguiKey, keyboard.IsKeyDown(key));
        }

        io.DisplaySize = new System.Numerics.Vector2(
            _graphicsDevice.PresentationParameters.BackBufferWidth,
            _graphicsDevice.PresentationParameters.BackBufferHeight);
        io.DisplayFramebufferScale = System.Numerics.Vector2.One;
    }

    private bool TryMapKeys(Keys key, out ImGuiKey imguiKey)
    {
        if (key == Keys.None)
        {
            imguiKey = ImGuiKey.None;
            return true;
        }

        imguiKey = key switch
        {
            Keys.Back => ImGuiKey.Backspace,
            Keys.Tab => ImGuiKey.Tab,
            Keys.Enter => ImGuiKey.Enter,
            Keys.CapsLock => ImGuiKey.CapsLock,
            Keys.Escape => ImGuiKey.Escape,
            Keys.Space => ImGuiKey.Space,
            Keys.PageUp => ImGuiKey.PageUp,
            Keys.PageDown => ImGuiKey.PageDown,
            Keys.End => ImGuiKey.End,
            Keys.Home => ImGuiKey.Home,
            Keys.Left => ImGuiKey.LeftArrow,
            Keys.Right => ImGuiKey.RightArrow,
            Keys.Up => ImGuiKey.UpArrow,
            Keys.Down => ImGuiKey.DownArrow,
            Keys.PrintScreen => ImGuiKey.PrintScreen,
            Keys.Insert => ImGuiKey.Insert,
            Keys.Delete => ImGuiKey.Delete,
            >= Keys.D0 and <= Keys.D9 => ImGuiKey._0 + (key - Keys.D0),
            >= Keys.A and <= Keys.Z => ImGuiKey.A + (key - Keys.A),
            >= Keys.NumPad0 and <= Keys.NumPad9 => ImGuiKey.Keypad0 + (key - Keys.NumPad0),
            Keys.Multiply => ImGuiKey.KeypadMultiply,
            Keys.Add => ImGuiKey.KeypadAdd,
            Keys.Subtract => ImGuiKey.KeypadSubtract,
            Keys.Decimal => ImGuiKey.KeypadDecimal,
            Keys.Divide => ImGuiKey.KeypadDivide,
            >= Keys.F1 and <= Keys.F24 => ImGuiKey.F1 + (key - Keys.F1),
            Keys.NumLock => ImGuiKey.NumLock,
            Keys.Scroll => ImGuiKey.ScrollLock,
            Keys.LeftShift => ImGuiKey.LeftShift,
            Keys.RightShift => ImGuiKey.RightShift,
            Keys.LeftControl => ImGuiKey.LeftCtrl,
            Keys.RightControl => ImGuiKey.RightCtrl,
            Keys.LeftAlt => ImGuiKey.LeftAlt,
            Keys.RightAlt => ImGuiKey.RightAlt,
            Keys.OemSemicolon => ImGuiKey.Semicolon,
            Keys.OemPlus => ImGuiKey.Equal,
            Keys.OemComma => ImGuiKey.Comma,
            Keys.OemMinus => ImGuiKey.Minus,
            Keys.OemPeriod => ImGuiKey.Period,
            Keys.OemQuestion => ImGuiKey.Slash,
            Keys.OemTilde => ImGuiKey.GraveAccent,
            Keys.OemOpenBrackets => ImGuiKey.LeftBracket,
            Keys.OemCloseBrackets => ImGuiKey.RightBracket,
            Keys.OemPipe => ImGuiKey.Backslash,
            Keys.OemQuotes => ImGuiKey.Apostrophe,
            Keys.BrowserBack => ImGuiKey.AppBack,
            Keys.BrowserForward => ImGuiKey.AppForward,
            _ => ImGuiKey.None
        };

        return imguiKey != ImGuiKey.None;
    }

    // --- Rendering ---

    protected virtual Effect UpdateEffect(Texture2D texture)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        _effect ??= new BasicEffect(_graphicsDevice);

        _effect.World = Matrix.Identity;
        _effect.View = Matrix.Identity;
        _effect.Projection = Matrix.CreateOrthographicOffCenter(
            0f, io.DisplaySize.X, io.DisplaySize.Y, 0f, -1f, 1f);
        _effect.TextureEnabled = true;
        _effect.Texture = texture;
        _effect.VertexColorEnabled = true;

        return _effect;
    }

    private void RenderDrawData(ImDrawDataPtr drawData)
    {
        var lastViewport = _graphicsDevice.Viewport;
        var lastScissorBox = _graphicsDevice.ScissorRectangle;
        var lastRasterizer = _graphicsDevice.RasterizerState;
        var lastDepthStencil = _graphicsDevice.DepthStencilState;
        var lastBlendFactor = _graphicsDevice.BlendFactor;
        var lastBlendState = _graphicsDevice.BlendState;

        _graphicsDevice.BlendFactor = Color.White;
        _graphicsDevice.BlendState = BlendState.NonPremultiplied;
        _graphicsDevice.RasterizerState = _rasterizerState;
        _graphicsDevice.DepthStencilState = DepthStencilState.DepthRead;
        _graphicsDevice.Viewport = new Viewport(0, 0,
            _graphicsDevice.PresentationParameters.BackBufferWidth,
            _graphicsDevice.PresentationParameters.BackBufferHeight);

        drawData.ScaleClipRects(ImGui.GetIO().DisplayFramebufferScale);

        UpdateBuffers(drawData);
        RenderCommandLists(drawData);

        _graphicsDevice.Viewport = lastViewport;
        _graphicsDevice.ScissorRectangle = lastScissorBox;
        _graphicsDevice.RasterizerState = lastRasterizer;
        _graphicsDevice.DepthStencilState = lastDepthStencil;
        _graphicsDevice.BlendState = lastBlendState;
        _graphicsDevice.BlendFactor = lastBlendFactor;
    }

    private unsafe void UpdateBuffers(ImDrawDataPtr drawData)
    {
        if (drawData.TotalVtxCount == 0) return;

        if (drawData.TotalVtxCount > _vertexBufferSize)
        {
            _vertexBuffer?.Dispose();
            _vertexBufferSize = (int)(drawData.TotalVtxCount * 1.5f);
            _vertexBuffer = new VertexBuffer(_graphicsDevice, DrawVertDeclaration.Declaration, _vertexBufferSize, BufferUsage.None);
            _vertexData = new byte[_vertexBufferSize * DrawVertDeclaration.Size];
        }

        if (drawData.TotalIdxCount > _indexBufferSize)
        {
            _indexBuffer?.Dispose();
            _indexBufferSize = (int)(drawData.TotalIdxCount * 1.5f);
            _indexBuffer = new IndexBuffer(_graphicsDevice, IndexElementSize.SixteenBits, _indexBufferSize, BufferUsage.None);
            _indexData = new byte[_indexBufferSize * sizeof(ushort)];
        }

        int vtxOffset = 0;
        int idxOffset = 0;

        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            ImDrawListPtr cmdList = drawData.CmdLists[n];

            fixed (void* vtxDstPtr = &_vertexData[vtxOffset * DrawVertDeclaration.Size])
            fixed (void* idxDstPtr = &_indexData[idxOffset * sizeof(ushort)])
            {
                Buffer.MemoryCopy(
                    (void*)cmdList.VtxBuffer.Data,
                    vtxDstPtr,
                    _vertexData.Length,
                    cmdList.VtxBuffer.Size * DrawVertDeclaration.Size);
                Buffer.MemoryCopy(
                    (void*)cmdList.IdxBuffer.Data,
                    idxDstPtr,
                    _indexData.Length,
                    cmdList.IdxBuffer.Size * sizeof(ushort));
            }

            vtxOffset += cmdList.VtxBuffer.Size;
            idxOffset += cmdList.IdxBuffer.Size;
        }

        _vertexBuffer.SetData(_vertexData, 0, drawData.TotalVtxCount * DrawVertDeclaration.Size);
        _indexBuffer.SetData(_indexData, 0, drawData.TotalIdxCount * sizeof(ushort));
    }

    private unsafe void RenderCommandLists(ImDrawDataPtr drawData)
    {
        _graphicsDevice.SetVertexBuffer(_vertexBuffer);
        _graphicsDevice.Indices = _indexBuffer;

        int vtxOffset = 0;
        int idxOffset = 0;

        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            ImDrawListPtr cmdList = drawData.CmdLists[n];

            for (int cmdi = 0; cmdi < cmdList.CmdBuffer.Size; cmdi++)
            {
                ImDrawCmdPtr drawCmd = cmdList.CmdBuffer[cmdi];

                if (drawCmd.ElemCount == 0) continue;

                ulong texId = drawCmd.GetTexID();
                if (!_textures.TryGetValue(texId, out var textureInfo))
                    throw new InvalidOperationException($"Could not find texture '{texId}'");

                _graphicsDevice.ScissorRectangle = new Rectangle(
                    (int)drawCmd.ClipRect.X,
                    (int)drawCmd.ClipRect.Y,
                    (int)(drawCmd.ClipRect.Z - drawCmd.ClipRect.X),
                    (int)(drawCmd.ClipRect.W - drawCmd.ClipRect.Y));

                var effect = UpdateEffect(textureInfo.Texture);
                foreach (var pass in effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
#pragma warning disable CS0618
                    _graphicsDevice.DrawIndexedPrimitives(
                        primitiveType: PrimitiveType.TriangleList,
                        baseVertex: (int)drawCmd.VtxOffset + vtxOffset,
                        minVertexIndex: 0,
                        numVertices: cmdList.VtxBuffer.Size,
                        startIndex: (int)drawCmd.IdxOffset + idxOffset,
                        primitiveCount: (int)drawCmd.ElemCount / 3);
#pragma warning restore CS0618
                }
            }

            vtxOffset += cmdList.VtxBuffer.Size;
            idxOffset += cmdList.IdxBuffer.Size;
        }
    }

    public void Dispose()
    {
        _vertexBuffer?.Dispose();
        _indexBuffer?.Dispose();
        _effect?.Dispose();

        foreach (var info in _textures.Values)
            if (info.IsManaged) info.Texture?.Dispose();

        _textures.Clear();
        ImGui.DestroyContext();
    }
}