using System.Runtime.InteropServices;
using System.Text;
using BaoBao.Common.Json;

namespace BaoBaoPaddleOCR;

public interface IBaoBaoPaddleOcrClient
{
    OcrResult Detect(string imagePath, bool includeColor = false);
}

public sealed class BaoBaoPaddleOcrClientOptions
{
    public string? ModelRoot { get; init; }

    public string? NativeDir { get; init; }

    public bool EnableGpu { get; init; }

    public int GpuDeviceId { get; init; }

    public bool EnableMkldnn { get; init; } = true;

    public int CpuThreads { get; init; } = 8;
}

public sealed class BaoBaoPaddleOcrClient : IBaoBaoPaddleOcrClient, IDisposable
{
    private readonly nint _handle;
    private readonly NativeMethods.NativeExports _native;
    private bool _disposed;

    public BaoBaoPaddleOcrClient(string? modelRoot = null, string? nativeDir = null)
        : this(new BaoBaoPaddleOcrClientOptions
        {
            ModelRoot = modelRoot,
            NativeDir = nativeDir
        })
    {
    }

    public BaoBaoPaddleOcrClient(
        string? modelRoot = null,
        string? nativeDir = null,
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
        ArgumentNullException.ThrowIfNull(options);

        ApplyModelDirectoryCompatibilityOverrides();
        _native = NativeMethods.Load(options.NativeDir, options.EnableGpu);

        var resolvedModelRoot = ResolveModelRoot(options.ModelRoot);
        var nativeOptions = NativeMethods.BaoBaoPaddleOcrCreateOptions.Create(options);
        var code = _native.CreateWithOptions(resolvedModelRoot, nativeOptions, out _handle, out var errorPtr);
        var error = _native.ReadUtf8AndFree(errorPtr);

        if (code != 0 || _handle == nint.Zero)
        {
            throw new InvalidOperationException($"Failed to initialize PaddleOCR (code={code}). {error}");
        }
    }

    public static BaoBaoPaddleOcrClient Shared { get; } = new(new BaoBaoPaddleOcrClientOptions());

    public OcrResult Detect(string imagePath, bool includeColor = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(imagePath))
        {
            throw new ArgumentException("imagePath cannot be null or empty.", nameof(imagePath));
        }

        var fullPath = Path.GetFullPath(imagePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Image file not found: {fullPath}", fullPath);
        }

        var code = _native.Detect(_handle, fullPath, out var jsonPtr, out var errorPtr);
        var json = _native.ReadUtf8AndFree(jsonPtr);
        var error = _native.ReadUtf8AndFree(errorPtr);

        if (code != 0)
        {
            throw new InvalidOperationException($"OCR detection failed (code={code}). {error}");
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

        if (_handle != nint.Zero)
        {
            _native.Destroy(_handle);
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private static string ResolveModelRoot(string? modelRoot)
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

        return Path.Combine(AppContext.BaseDirectory, "models");
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
        var dto = JsonKit.Deserialize<NativeOcrResultDto>(json)
                  ?? throw new InvalidOperationException("Failed to deserialize OCR JSON result.");

        var blocks = dto.Blocks is null
            ? Array.Empty<OcrBlock>()
            : dto.Blocks.Select(static x => new OcrBlock(
                x.Text ?? string.Empty,
                x.Score,
                x.Box is null
                    ? Array.Empty<OcrPoint>()
                    : x.Box
                        .Where(static point => point.Length >= 2)
                        .Select(static point => new OcrPoint(point[0], point[1]))
                        .ToArray(),
                TryParseNativeColorSample(x)))
                .ToArray();

        if (!includeColor)
        {
            blocks = blocks.Select(static block => block with { ColorSample = null }).ToArray();
        }

        var text = string.IsNullOrWhiteSpace(dto.Text)
            ? string.Join(Environment.NewLine, blocks.Select(x => x.Text).Where(x => !string.IsNullOrWhiteSpace(x)))
            : dto.Text;

        return new OcrResult(text, json, blocks);
    }

    private static OcrColorSample? TryParseNativeColorSample(NativeOcrBlockDto dto)
    {
        if (dto.SampleRgb is not { Length: >= 3 })
        {
            return null;
        }

        var red = dto.SampleRgb[0];
        var green = dto.SampleRgb[1];
        var blue = dto.SampleRgb[2];
        var variance = dto.SampleVariance ?? 0f;
        var hex = string.IsNullOrWhiteSpace(dto.SampleColorHex)
            ? $"#{(int)Math.Clamp(Math.Round(red), 0, 255):X2}{(int)Math.Clamp(Math.Round(green), 0, 255):X2}{(int)Math.Clamp(Math.Round(blue), 0, 255):X2}"
            : dto.SampleColorHex!;
        return new OcrColorSample(hex, red, green, blue, variance);
    }
}

internal sealed class NativeOcrResultDto
{
    public string? Text { get; set; }

    public NativeOcrBlockDto[]? Blocks { get; set; }
}

internal sealed class NativeOcrBlockDto
{
    public string? Text { get; set; }

    public float Score { get; set; }

    public float[][]? Box { get; set; }

    public string? SampleColorHex { get; set; }

    public float[]? SampleRgb { get; set; }

    public float? SampleVariance { get; set; }
}

internal static partial class NativeMethods
{
    private const string CpuDllName = "BaoBaoPaddleOCR.Native.Cpu";
    private const string GpuDllName = "BaoBaoPaddleOCR.Native.Gpu";
    private static NativeExports? _cpuExports;
    private static NativeExports? _gpuExports;

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
                StructSize = Marshal.SizeOf<BaoBaoPaddleOcrCreateOptions>(),
                EnableGpu = options.EnableGpu ? 1 : 0,
                GpuDeviceId = options.GpuDeviceId,
                EnableMkldnn = options.EnableMkldnn ? 1 : 0,
                CpuThreads = options.CpuThreads
            };
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int CreateWithOptionsDelegate(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string modelRoot,
        BaoBaoPaddleOcrCreateOptions options,
        out nint handle,
        out nint errorMessage);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int DetectDelegate(
        nint handle,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string imagePath,
        out nint jsonResult,
        out nint errorMessage);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void DestroyDelegate(nint handle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void FreeDelegate(nint textPtr);

    internal sealed class NativeExports
    {
        private readonly FreeDelegate _free;

        public NativeExports(
            nint libraryHandle,
            CreateWithOptionsDelegate createWithOptions,
            DetectDelegate detect,
            DestroyDelegate destroy,
            FreeDelegate free)
        {
            LibraryHandle = libraryHandle;
            CreateWithOptions = createWithOptions;
            Detect = detect;
            Destroy = destroy;
            _free = free;
        }

        public nint LibraryHandle { get; }

        public CreateWithOptionsDelegate CreateWithOptions { get; }

        public DetectDelegate Detect { get; }

        public DestroyDelegate Destroy { get; }

        public string ReadUtf8AndFree(nint ptr)
        {
            if (ptr == nint.Zero)
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

    public static NativeExports Load(string? nativeDir, bool enableGpu)
    {
        if (enableGpu)
        {
            return _gpuExports ??= LoadCore(nativeDir, GpuDllName);
        }

        return _cpuExports ??= LoadCore(nativeDir, CpuDllName);
    }

    private static NativeExports LoadCore(string? nativeDir, string dllName)
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
            var libraryHandle = NativeLibrary.Load(dllPath);
            return new NativeExports(
                libraryHandle,
                GetDelegate<CreateWithOptionsDelegate>(libraryHandle, "BaoBaoPaddleOcr_CreateWithOptions"),
                GetDelegate<DetectDelegate>(libraryHandle, "BaoBaoPaddleOcr_Detect"),
                GetDelegate<DestroyDelegate>(libraryHandle, "BaoBaoPaddleOcr_Destroy"),
                GetDelegate<FreeDelegate>(libraryHandle, "BaoBaoPaddleOcr_Free"));
        }

        throw new DllNotFoundException(
            $"Could not find {dllName}.dll. Set BAOBAO_PADDLEOCR_NATIVE_DIR or pass nativeDir to BaoBaoPaddleOcrClient.");
    }

    private static T GetDelegate<T>(nint libraryHandle, string exportName) where T : Delegate
    {
        var exportPtr = NativeLibrary.GetExport(libraryHandle, exportName);
        return Marshal.GetDelegateForFunctionPointer<T>(exportPtr);
    }

    private static void TryRegisterDllDirectory(string fullDir)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        SetDefaultDllDirectories(LoadLibrarySearchFlags.LOAD_LIBRARY_SEARCH_DEFAULT_DIRS | LoadLibrarySearchFlags.LOAD_LIBRARY_SEARCH_USER_DIRS);
        AddDllDirectory(fullDir);
        SetDllDirectory(fullDir);
    }

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool SetDefaultDllDirectories(LoadLibrarySearchFlags directoryFlags);

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint AddDllDirectory(string newDirectory);

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetDllDirectory(string? lpPathName);

    [Flags]
    private enum LoadLibrarySearchFlags : uint
    {
        LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000,
        LOAD_LIBRARY_SEARCH_USER_DIRS = 0x00000400
    }

    private static IEnumerable<string> GetNativeSearchPaths(string? nativeDir)
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

        yield return AppContext.BaseDirectory;
        yield return Path.Combine(AppContext.BaseDirectory, "native");
        yield return Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native");

        var assemblyDir = Path.GetDirectoryName(typeof(BaoBaoPaddleOcrClient).Assembly.Location);
        if (!string.IsNullOrWhiteSpace(assemblyDir))
        {
            yield return assemblyDir;
            yield return Path.Combine(assemblyDir, "native");
            yield return Path.Combine(assemblyDir, "runtimes", "win-x64", "native");
        }
    }
}
