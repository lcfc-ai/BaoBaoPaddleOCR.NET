using System.Runtime.Versioning;
using System.Text.Json;
using BaoBaoPaddleOCR;
using BaoBaoPaddleOCR.Cli;

[assembly: SupportedOSPlatform("windows")]

var exitCode = CliRunner.Run(args);
return exitCode;

internal static class CliRunner
{
    [SupportedOSPlatform("windows")]
    public static int Run(string[] args)
    {
        if (args.Length == 0 || HasFlag(args, "--help", "-h"))
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        var imagePath = args[0];
        var modelRoot = GetOption(args, "--model-root");
        var nativeDir = GetOption(args, "--native-dir");
        var outputJson = HasFlag(args, "--json");
        var outputFull = HasFlag(args, "--full");
        var parseWechat = HasFlag(args, "--wechat");

        try
        {
            using var client = new BaoBaoPaddleOcrClient(modelRoot, nativeDir);

            if (parseWechat)
            {
                var chat = WechatConversationParser.Parse(
                    imagePath,
                    client.Detect(imagePath, includeColor: true));

                if (outputFull)
                {
                    Console.WriteLine(JsonSerializer.Serialize(chat, JsonOptions));
                    return 0;
                }

                PrintWechatMessages(chat);
                return 0;
            }

            var result = client.Detect(imagePath);

            if (outputFull)
            {
                Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
                return 0;
            }

            if (outputJson)
            {
                Console.WriteLine(result.JsonText);
                return 0;
            }

            Console.WriteLine(result.Text);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"OCR failed: {ex.Message}");
            return 2;
        }
    }

    private static bool HasFlag(IEnumerable<string> args, params string[] flags)
    {
        return args.Any(arg => flags.Any(flag => string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase)));
    }

    private static string? GetOption(string[] args, string optionName)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static object NormalizeWechatMessages(WechatConversationParseResult chat)
    {
        var timeline = new List<object>();
        string? currentTime = null;

        foreach (var message in chat.Messages)
        {
            if (message.MessageKind == WechatMessageKind.Time)
            {
                currentTime = message.Text;
                timeline.Add(new
                {
                    type = "time",
                    text = message.Text,
                    confidence = Math.Round(message.Confidence, 4)
                });
                continue;
            }

            if (message.MessageKind == WechatMessageKind.System)
            {
                timeline.Add(new
                {
                    type = "system",
                    text = message.Text,
                    time = currentTime,
                    confidence = Math.Round(message.Confidence, 4)
                });
                continue;
            }

            timeline.Add(new
            {
                type = message.MessageKind == WechatMessageKind.Media ? "media" : "message",
                speaker = message.Speaker,
                text = message.Text,
                time = currentTime,
                side = message.Side.ToString(),
                bubbleColor = message.BubbleColor.ToString(),
                confidence = Math.Round(message.Confidence, 4),
                bounds = new
                {
                    left = message.Bounds.Left,
                    top = message.Bounds.Top,
                    right = message.Bounds.Right,
                    bottom = message.Bounds.Bottom
                }
            });
        }

        return new
        {
            type = "wechat_conversation",
            messages = timeline
        };
    }

    private static void PrintWechatMessages(WechatConversationParseResult chat)
    {
        string? currentTime = null;

        foreach (var message in chat.Messages)
        {
            if (message.MessageKind == WechatMessageKind.Time)
            {
                currentTime = message.Text;
                Console.WriteLine($"[时间] {message.Text}");
                continue;
            }

            if (message.MessageKind == WechatMessageKind.System)
            {
                if (!string.IsNullOrWhiteSpace(currentTime))
                {
                    Console.WriteLine($"[系统][{currentTime}] {message.Text}");
                }
                else
                {
                    Console.WriteLine($"[系统] {message.Text}");
                }
                continue;
            }

            var kind = message.MessageKind == WechatMessageKind.Media ? "媒体" : "消息";
            var prefix = string.IsNullOrWhiteSpace(currentTime)
                ? $"[{kind}][{message.Side}] {message.Speaker}"
                : $"[{kind}][{message.Side}][{currentTime}] {message.Speaker}";

            Console.WriteLine($"{prefix}: {message.Text}");
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  BaoBaoPaddleOCR.Cli <imagePath> [--model-root <dir>] [--native-dir <dir>] [--json|--full] [--wechat]");
        Console.WriteLine();
        Console.WriteLine("Environment:");
        Console.WriteLine("  BAOBAO_PADDLEOCR_NATIVE_DIR  Native DLL 目录（可选）");
        Console.WriteLine("  BAOBAO_PADDLEOCR_MODEL_ROOT  模型目录（可选）");
        Console.WriteLine("  BAOBAO_PADDLEOCR_MOCK_TEXT   Mock 输出文本（可选）");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
}
