// Minimal SDL2 bindings (only what the front end needs). SDL2 is zlib-licensed; the native library is loaded at run time.
using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace DigDug
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SdlRect { public int X, Y, W, H; }

    [StructLayout(LayoutKind.Sequential)]
    public struct SdlAudioSpec
    {
        public int Freq; public ushort Format; public byte Channels, Silence; public ushort Samples, Padding;
        public uint Size; public IntPtr Callback, UserData;
    }

    public static unsafe class Sdl
    {
        const string Lib = "SDL2";

        static Sdl()
        {
            NativeLibrary.SetDllImportResolver(typeof(Sdl).Assembly, Resolve);
        }

        static IntPtr Resolve(string name, Assembly asm, DllImportSearchPath? path)
        {
            if (name != Lib) return IntPtr.Zero;
            string[] candidates = { "SDL2", "libSDL2-2.0.so.0", "libSDL2.so", "libSDL2-2.0.0.dylib", "libSDL2.dylib" };
            foreach (var c in candidates)
            {
                IntPtr h;
                if (NativeLibrary.TryLoad(c, asm, path, out h)) return h;
                if (NativeLibrary.TryLoad(c, out h)) return h;
            }
            return IntPtr.Zero;
        }

        public const uint InitAudio = 0x10, InitVideo = 0x20, InitGameController = 0x2000, InitEvents = 0x4000;
        public const uint WindowResizable = 0x20, WindowFullscreenDesktop = 0x1001;
        public const uint RendererAccelerated = 2, RendererPresentVSync = 4;
        public const uint PixelFormatArgb8888 = 372645892;
        public const int TextureAccessStreaming = 1;
        public const ushort AudioS16 = 0x8010;
        public const int WindowPosCentered = 0x2FFF0000;

        public const uint EvQuit = 0x100, EvWindow = 0x200, EvKeyDown = 0x300, EvKeyUp = 0x301;
        public const uint EvMouseButtonDown = 0x401;
        public const uint EvControllerAxis = 0x650, EvControllerButtonUp = 0x652;
        public const uint EvControllerButtonDown = 0x651, EvControllerDeviceAdded = 0x653, EvControllerDeviceRemoved = 0x654;
        public const uint EvDropFile = 0x1000;

        // controller buttons / axes
        public const int BtnA = 0, BtnB = 1, BtnX = 2, BtnY = 3, BtnBack = 4, BtnGuide = 5, BtnStart = 6, BtnLStick = 7, BtnRStick = 8,
            BtnLShoulder = 9, BtnRShoulder = 10, BtnUp = 11, BtnDown = 12, BtnLeft = 13, BtnRight = 14;
        public const int AxisLX = 0, AxisLY = 1, AxisTriggerLeft = 4, AxisTriggerRight = 5;

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_Init(uint flags);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_Quit();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SDL_GetError();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_SetHint([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_CreateWindow([MarshalAs(UnmanagedType.LPUTF8Str)] string title, int x, int y, int w, int h, uint flags);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_DestroyWindow(IntPtr win);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_SetWindowFullscreen(IntPtr win, uint flags);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_SetWindowSize(IntPtr win, int w, int h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_SetWindowPosition(IntPtr win, int x, int y);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_SetWindowTitle(IntPtr win, [MarshalAs(UnmanagedType.LPUTF8Str)] string title);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_GetWindowSize(IntPtr win, out int w, out int h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_GetDisplayUsableBounds(int display, out SdlRect rect);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_CreateRenderer(IntPtr win, int index, uint flags);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_DestroyRenderer(IntPtr r);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_CreateTexture(IntPtr r, uint format, int access, int w, int h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_DestroyTexture(IntPtr t);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_UpdateTexture(IntPtr t, IntPtr rect, IntPtr pixels, int pitch);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_SetRenderDrawColor(IntPtr r, byte R, byte G, byte B, byte A);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_RenderClear(IntPtr r);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_RenderCopy(IntPtr r, IntPtr tex, IntPtr src, ref SdlRect dst);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_RenderPresent(IntPtr r);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_GetRendererOutputSize(IntPtr r, out int w, out int h);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_PollEvent(IntPtr ev);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_GetKeyboardState(out int numkeys);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_ShowCursor(int toggle);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_Delay(uint ms);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_free(IntPtr p);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_ShowSimpleMessageBox(uint flags, [MarshalAs(UnmanagedType.LPUTF8Str)] string title, [MarshalAs(UnmanagedType.LPUTF8Str)] string message, IntPtr win);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern uint SDL_OpenAudioDevice(IntPtr device, int capture, ref SdlAudioSpec desired, out SdlAudioSpec obtained, int allowedChanges);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_PauseAudioDevice(uint dev, int pause);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_QueueAudio(uint dev, IntPtr data, uint len);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern uint SDL_GetQueuedAudioSize(uint dev);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_ClearQueuedAudio(uint dev);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_CloseAudioDevice(uint dev);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_NumJoysticks();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_IsGameController(int index);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_GameControllerOpen(int index);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_GameControllerClose(IntPtr pad);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_GameControllerGetButton(IntPtr pad, int button);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern short SDL_GameControllerGetAxis(IntPtr pad, int axis);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SDL_GameControllerName(IntPtr pad);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SDL_GameControllerGetJoystick(IntPtr pad);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern int SDL_JoystickInstanceID(IntPtr joy);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SDL_GetCurrentAudioDriver();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_EventState(uint type, int state);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_GetNumAudioDevices(int capture);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_GetAudioDeviceName(int index, int capture);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_PushEvent(IntPtr ev);
        // rendering extras: rotation, overlays, offscreen targets (for screenshots), window features, rumble, key names
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_RenderCopyEx(IntPtr r, IntPtr tex, IntPtr src, ref SdlRect dst, double angle, IntPtr center, int flip);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_SetTextureBlendMode(IntPtr tex, int mode);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_SetTextureScaleMode(IntPtr tex, int mode);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_SetRenderTarget(IntPtr r, IntPtr tex);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_RenderReadPixels(IntPtr r, IntPtr rect, uint format, IntPtr pixels, int pitch);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_SetWindowBordered(IntPtr win, int bordered);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_SetWindowAlwaysOnTop(IntPtr win, int onTop);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_RenderSetVSync(IntPtr r, int vsync);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_GameControllerRumble(IntPtr pad, ushort low, ushort high, uint ms);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SDL_GetScancodeName(int sc);
        public const int BlendModeBlend = 1, TextureAccessTarget = 2, ScaleNearest = 0, ScaleLinear = 1;
        public const int EvWindowFocusGained = 12, EvWindowFocusLost = 13;
        public static string ScancodeName(int sc) { return sc <= 0 ? "NONE" : (Marshal.PtrToStringUTF8(SDL_GetScancodeName(sc)) ?? "?"); }

        public static string AudioDriver() { return Marshal.PtrToStringUTF8(SDL_GetCurrentAudioDriver()) ?? "?"; }
        public static string Error() { return Marshal.PtrToStringUTF8(SDL_GetError()) ?? ""; }
        public static string ControllerName(IntPtr pad) { return Marshal.PtrToStringUTF8(SDL_GameControllerName(pad)) ?? "Controller"; }
        public static int InstanceId(IntPtr pad) { return SDL_JoystickInstanceID(SDL_GameControllerGetJoystick(pad)); }
    }
}
