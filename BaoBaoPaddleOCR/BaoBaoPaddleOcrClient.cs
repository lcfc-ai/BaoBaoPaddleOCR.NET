using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json;

namespace BaoBaoPaddleOCR
{
    public interface IBaoBaoPaddleOcrClient
    {
        OcrResult Detect(string imagePath, bool includeColor = false);
    }

    public sealed class BaoBaoPaddleOcrClientOptions
    {
        public BaoBaoPaddleOcrClientOptions()
        {
            EnableMkldnn = true;
            CpuThreads = 8;
        }

        public string ModelRoot { get; set; }

        public string NativeDir { get; set; }

        public bool EnableGpu { get; set; }

        public int GpuDeviceId { get; set; }

        public bool EnableMkldnn { get; set; }

        public int CpuThreads { get; set; }
    }

    public sealed class BaoBaoPaddleOcrClient : IBaoBaoPaddleOcrClient, IDisposable
    {
        private static readonly object SharedSyncRoot = new object();
        private static BaoBaoPaddleOcrClient _shared;

        private readonly IntPtr _handle;
        private readonly NativeMethods.NativeExports _native;
        private bool _disposed;

        public BaoBaoPaddleOcrClient()
            : this(new BaoBaoPaddleOcrClientOptions())
        {
        }

        public BaoBaoPaddleOcrClient(string modelRoot, string nativeDir)
            : this(new BaoBaoPaddleOcrClientOptions
            {
                ModelRoot = modelRoot,
                NativeDir = nativeDir
            })
        {
        }

        public BaoBaoPaddleOcrClient(
            string modelRoot = null,
            string nativeDir = null,
            bool enableGpu = false,
            int gpuDeviceId = 0,
            bool enableMkldnn = true,
            int cpuThreads = 8)
            : this(new BaoBaoPaddleOcrClientOptions
            {
                ModelRoot = modelRoot,
                NativeDir = nativeDir,
                EnableGpu = enableGpu,
                GpuDeviceId = gpuDeviceId,
                EnableMkldnn = enableMkldnn,
                CpuThreads = cpuThreads
            })
        {
        }

        public BaoBaoPaddleOcrClient(BaoBaoPaddleOcrClientOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }

            ApplyModelDirectoryCompatibilityOverrides();
            _native = NativeMethods.Load(options.NativeDir, options.EnableGpu);

            var resolvedModelRoot = ResolveModelRoot(options.ModelRoot);
            var nativeOptions = NativeMethods.BaoBaoPaddleOcrCreateOptions.Create(options);
            IntPtr errorPtr;
            var code = _native.CreateWithOptions(resolvedModelRoot, ref nativeOptions, out _handle, out errorPtr);
            var error = _native.ReadUtf8AndFree(errorPtr);

            if (code != 0 || _handle == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    string.Format("Failed to initialize PaddleOCR (code={0}). {1}", code, error));
            }
        }

        public static BaoBaoPaddleOcrClient Shared
        {
            get
            {
                lock (SharedSyncRoot)
                {
                    if (_shared == null)
                    {
                        _shared = new BaoBaoPaddleOcrClient(new BaoBaoPaddleOcrClientOptions());
                    }

                    return _shared;
                }
            }
        }

        public OcrResult Detect(string imagePath, bool includeColor = false)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(imagePath))
            {
                throw new ArgumentException("imagePath cannot be null or empty.", "imagePath");
            }

            var fullPath = Path.GetFullPath(imagePath);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("Image file not found: " + fullPath, fullPath);
            }

            IntPtr jsonPtr;
            IntPtr errorPtr;
            var code = _native.Detect(_handle, fullPath, out jsonPtr, out errorPtr);
            var json = _native.ReadUtf8AndFree(jsonPtr);
            var error = _native.ReadUtf8AndFree(errorPtr);

            if (code != 0)
            {
                throw new InvalidOperationException(
                    string.Format("OCR detection failed (code={0}). {1}", code, error));
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException("OCR returned an empty JSON payload.");
            }

            return OcrResultParser.Parse(json, includeColor);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (_handle != IntPtr.Zero)
            {
                _native.Destroy(_handle);
            }

            _disposed = true;
            GC.SuppressFinalize(this);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }

        private static string ResolveModelRoot(string modelRoot)
        {
            if (!string.IsNullOrWhiteSpace(modelRoot))
            {
                return Path.GetFullPath(modelRoot);
            }

            var fromEnv = Environment.GetEnvironmentVariable("BAOBAO_PADDLEOCR_MODEL_ROOT");
            if (!string.IsNullOrWhiteSpace(fromEnv))
            {
                return Path.GetFullPath(fromEnv);
            }

            return Path.Combine(PathHelpers.GetBaseDirectory(), "models");
        }

        private static void ApplyModelDirectoryCompatibilityOverrides()
        {
            SetEnvIfMissing("BAOBAO_PADDLEOCR_DET_DIRNAME", "PP-OCRv5_mobile_det_infer");
            SetEnvIfMissing("BAOBAO_PADDLEOCR_REC_DIRNAME", "PP-OCRv5_mobile_rec_infer");
            SetEnvIfMissing("BAOBAO_PADDLEOCR_CLS_DIRNAME", "PP-LCNet_x1_0_textline_ori_infer");
        }

        private static void SetEnvIfMissing(string name, string value)
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
            {
                return;
            }

            Environment.SetEnvironmentVariable(name, value);
        }
    }

    internal static class OcrResultParser
    {
        public static OcrResult Parse(string json, bool includeColor)
        {
            var dto = JsonConvert.DeserializeObject<NativeOcrResultDto>(json);
            if (dto == null)
            {
                throw new InvalidOperationException("Failed to deserialize OCR JSON result.");
            }

            var blocks = dto.Blocks == null
                ? new OcrBlock[0]
                : dto.Blocks.Select(
                        x => new OcrBlock(
                            x.Text ?? string.Empty,
                            x.Score,
                            x.Box == null
                                ? new OcrPoint[0]
                                : x.Box
                                    .Where(point => point != null && point.Length >= 2)
                                    .Select(point => new OcrPoint(point[0], point[1]))
                                    .ToArray(),
                            TryParseNativeColorSample(x)))
                    .ToArray();

            if (!includeColor)
            {
                blocks = blocks
                    .Select(block => new OcrBlock(block.Text, block.Score, block.Box, null))
                    .ToArray();
            }

            var text = string.IsNullOrWhiteSpace(dto.Text)
                ? string.Join(Environment.NewLine, blocks.Select(x => x.Text).Where(x => !string.IsNullOrWhiteSpace(x)))
                : dto.Text;

            return new OcrResult(text, json, blocks);
        }

        private static OcrColorSample TryParseNativeColorSample(NativeOcrBlockDto dto)
        {
            if (dto.SampleRgb == null || dto.SampleRgb.Length < 3)
            {
                return null;
            }

            var red = dto.SampleRgb[0];
            var green = dto.SampleRgb[1];
            var blue = dto.SampleRgb[2];
            var variance = dto.SampleVariance ?? 0f;
            var hex = string.IsNullOrWhiteSpace(dto.SampleColorHex)
                ? string.Format(
                    "#{0:X2}{1:X2}{2:X2}",
                    ClampColor(red),
                    ClampColor(green),
                    ClampColor(blue))
                : dto.SampleColorHex;

            return new OcrColorSample(hex, red, green, blue, variance);
        }

        private static int ClampColor(float value)
        {
            var rounded = (int)Math.Round(value);
            if (rounded < 0)
            {
                return 0;
            }

            if (rounded > 255)
            {
                return 255;
            }

            return rounded;
        }
    }

    internal sealed class NativeOcrResultDto
    {
        public string Text { get; set; }

        public NativeOcrBlockDto[] Blocks { get; set; }
    }

    internal sealed class NativeOcrBlockDto
    {
        public string Text { get; set; }

        public float Score { get; set; }

        public float[][] Box { get; set; }

        public string SampleColorHex { get; set; }

        public float[] SampleRgb { get; set; }

        public float? SampleVariance { get; set; }
    }

    internal static class NativeMethods
    {
        private const string CpuDllName = "BaoBaoPaddleOCR.Native.Cpu";
        private const string GpuDllName = "BaoBaoPaddleOCR.Native.Gpu";

        private static readonly object SyncRoot = new object();
        private static NativeExports _cpuExports;
        private static NativeExports _gpuExports;
        private static string _cpuKey;
        private static string _gpuKey;

        [StructLayout(LayoutKind.Sequential)]
        internal struct BaoBaoPaddleOcrCreateOptions
        {
            public int StructSize;
            public int EnableGpu;
            public int GpuDeviceId;
            public int EnableMkldnn;
            public int CpuThreads;

            public static BaoBaoPaddleOcrCreateOptions Create(BaoBaoPaddleOcrClientOptions options)
            {
                return new BaoBaoPaddleOcrCreateOptions
                {
                    StructSize = Marshal.SizeOf(typeof(BaoBaoPaddleOcrCreateOptions)),
                    EnableGpu = options.EnableGpu ? 1 : 0,
                    GpuDeviceId = options.GpuDeviceId,
                    EnableMkldnn = options.EnableMkldnn ? 1 : 0,
                    CpuThreads = options.CpuThreads
                };
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int CreateWithOptionsDelegate(
            IntPtr modelRoot,
            ref BaoBaoPaddleOcrCreateOptions options,
            out IntPtr handle,
            out IntPtr errorMessage);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int DetectDelegate(
            IntPtr handle,
            IntPtr imagePath,
            out IntPtr jsonResult,
            out IntPtr errorMessage);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void DestroyDelegate(IntPtr handle);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void FreeDelegate(IntPtr textPtr);

        internal sealed class NativeExports
        {
            private readonly FreeDelegate _free;

            public NativeExports(
                IntPtr libraryHandle,
                CreateWithOptionsDelegate createWithOptions,
                DetectDelegate detect,
                DestroyDelegate destroy,
                FreeDelegate free)
            {
                LibraryHandle = libraryHandle;
                CreateWithOptionsCore = createWithOptions;
                DetectCore = detect;
                Destroy = destroy;
                _free = free;
            }

            public IntPtr LibraryHandle { get; private set; }

            public CreateWithOptionsDelegate CreateWithOptionsCore { get; private set; }

            public DetectDelegate DetectCore { get; private set; }

            public DestroyDelegate Destroy { get; private set; }

            public int CreateWithOptions(
                string modelRoot,
                ref BaoBaoPaddleOcrCreateOptions options,
                out IntPtr handle,
                out IntPtr errorMessage)
            {
                using (var modelRootUtf8 = Utf8StringHandle.Create(modelRoot))
                {
                    return CreateWithOptionsCore(modelRootUtf8.Pointer, ref options, out handle, out errorMessage);
                }
            }

            public int Detect(
                IntPtr handle,
                string imagePath,
                out IntPtr jsonResult,
                out IntPtr errorMessage)
            {
                using (var imagePathUtf8 = Utf8StringHandle.Create(imagePath))
                {
                    return DetectCore(handle, imagePathUtf8.Pointer, out jsonResult, out errorMessage);
                }
            }

            public string ReadUtf8AndFree(IntPtr ptr)
            {
                if (ptr == IntPtr.Zero)
                {
                    return string.Empty;
                }

                try
                {
                    var len = 0;
                    while (Marshal.ReadByte(ptr, len) != 0)
                    {
                        len++;
                    }

                    var bytes = new byte[len];
                    Marshal.Copy(ptr, bytes, 0, len);
                    return Encoding.UTF8.GetString(bytes);
                }
                finally
                {
                    _free(ptr);
                }
            }
        }

        public static NativeExports Load(string nativeDir, bool enableGpu)
        {
            var cacheKey = string.Join("|", GetNativeSearchPaths(nativeDir).Select(Path.GetFullPath));

            lock (SyncRoot)
            {
                if (enableGpu)
                {
                    if (_gpuExports == null || !string.Equals(_gpuKey, cacheKey, StringComparison.OrdinalIgnoreCase))
                    {
                        _gpuExports = LoadCore(nativeDir, GpuDllName);
                        _gpuKey = cacheKey;
                    }

                    return _gpuExports;
                }

                if (_cpuExports == null || !string.Equals(_cpuKey, cacheKey, StringComparison.OrdinalIgnoreCase))
                {
                    _cpuExports = LoadCore(nativeDir, CpuDllName);
                    _cpuKey = cacheKey;
                }

                return _cpuExports;
            }
        }

        private static NativeExports LoadCore(string nativeDir, string dllName)
        {
            foreach (var dir in GetNativeSearchPaths(nativeDir))
            {
                if (string.IsNullOrWhiteSpace(dir))
                {
                    continue;
                }

                var fullDir = Path.GetFullPath(dir);
                var dllPath = Path.Combine(fullDir, dllName + ".dll");
                if (!File.Exists(dllPath))
                {
                    continue;
                }

                TryRegisterDllDirectory(fullDir);
                var libraryHandle = LoadLibrary(dllPath);
                if (libraryHandle == IntPtr.Zero)
                {
                    throw new DllNotFoundException("Failed to load native library: " + dllPath);
                }

                return new NativeExports(
                    libraryHandle,
                    GetDelegate<CreateWithOptionsDelegate>(libraryHandle, "BaoBaoPaddleOcr_CreateWithOptions"),
                    GetDelegate<DetectDelegate>(libraryHandle, "BaoBaoPaddleOcr_Detect"),
                    GetDelegate<DestroyDelegate>(libraryHandle, "BaoBaoPaddleOcr_Destroy"),
                    GetDelegate<FreeDelegate>(libraryHandle, "BaoBaoPaddleOcr_Free"));
            }

            throw new DllNotFoundException(
                string.Format(
                    "Could not find {0}.dll. Set BAOBAO_PADDLEOCR_NATIVE_DIR or pass nativeDir to BaoBaoPaddleOcrClient.",
                    dllName));
        }

        private static T GetDelegate<T>(IntPtr libraryHandle, string exportName) where T : class
        {
            var exportPtr = GetProcAddress(libraryHandle, exportName);
            if (exportPtr == IntPtr.Zero)
            {
                throw new EntryPointNotFoundException("Cannot find export: " + exportName);
            }

            return (T)(object)Marshal.GetDelegateForFunctionPointer(exportPtr, typeof(T));
        }

        private static void TryRegisterDllDirectory(string fullDir)
        {
            try
            {
                SetDefaultDllDirectories(LoadLibrarySearchFlags.LOAD_LIBRARY_SEARCH_DEFAULT_DIRS | LoadLibrarySearchFlags.LOAD_LIBRARY_SEARCH_USER_DIRS);
                AddDllDirectory(fullDir);
                SetDllDirectory(fullDir);
            }
            catch
            {
            }
        }

        private static IEnumerable<string> GetNativeSearchPaths(string nativeDir)
        {
            if (!string.IsNullOrWhiteSpace(nativeDir))
            {
                yield return nativeDir;
            }

            var fromEnv = Environment.GetEnvironmentVariable("BAOBAO_PADDLEOCR_NATIVE_DIR");
            if (!string.IsNullOrWhiteSpace(fromEnv))
            {
                yield return fromEnv;
            }

            var baseDirectory = PathHelpers.GetBaseDirectory();
            yield return baseDirectory;
            yield return Path.Combine(baseDirectory, "native");
            yield return Path.Combine(baseDirectory, "runtimes", "win-x64", "native");

            var assemblyDir = Path.GetDirectoryName(typeof(BaoBaoPaddleOcrClient).Assembly.Location);
            if (!string.IsNullOrWhiteSpace(assemblyDir))
            {
                yield return assemblyDir;
                yield return Path.Combine(assemblyDir, "native");
                yield return Path.Combine(assemblyDir, "runtimes", "win-x64", "native");
            }
        }

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string fileName);

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr module, string procName);

        [DllImport("kernel32", SetLastError = true)]
        private static extern bool SetDefaultDllDirectories(LoadLibrarySearchFlags directoryFlags);

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr AddDllDirectory(string newDirectory);

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetDllDirectory(string pathName);

        [Flags]
        private enum LoadLibrarySearchFlags : uint
        {
            LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000,
            LOAD_LIBRARY_SEARCH_USER_DIRS = 0x00000400
        }
    }

    internal sealed class Utf8StringHandle : IDisposable
    {
        private IntPtr _pointer;

        private Utf8StringHandle(IntPtr pointer)
        {
            _pointer = pointer;
        }

        public IntPtr Pointer
        {
            get { return _pointer; }
        }

        public static Utf8StringHandle Create(string value)
        {
            if (value == null)
            {
                return new Utf8StringHandle(IntPtr.Zero);
            }

            var bytes = Encoding.UTF8.GetBytes(value);
            var pointer = Marshal.AllocHGlobal(bytes.Length + 1);
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
            Marshal.WriteByte(pointer, bytes.Length, 0);
            return new Utf8StringHandle(pointer);
        }

        public void Dispose()
        {
            if (_pointer == IntPtr.Zero)
            {
                return;
            }

            Marshal.FreeHGlobal(_pointer);
            _pointer = IntPtr.Zero;
        }
    }

    internal static class PathHelpers
    {
        public static string GetBaseDirectory()
        {
            return AppDomain.CurrentDomain.BaseDirectory;
        }
    }
}
