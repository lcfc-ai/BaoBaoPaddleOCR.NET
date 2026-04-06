using System.Drawing;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using BaoBaoPaddleOCR;

namespace BaoBaoPaddleOCR.Cli;

[SupportedOSPlatform("windows")]
internal static partial class WechatConversationParser
{
    private const float MinimumUsefulScore = 0.50f;
    private const int DefaultMarginX = 10;
    private const int DefaultMarginY = 6;
    private const float LeftThresholdRatio = 0.42f;
    private const float RightThresholdRatio = 0.58f;
    private const float SameMessageGap = 48f;
    private const float MediaMessageGap = 320f;
    private const float NicknameAttachGap = 64f;

    public static WechatConversationParseResult Parse(string imagePath, OcrResult ocrResult, WechatParseOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        ArgumentNullException.ThrowIfNull(ocrResult);

        using var bitmap = new Bitmap(imagePath);

        var analyzedBlocks = ocrResult.Blocks
            .Select((block, index) => AnalyzeBlock(bitmap, block, index, options))
            .Where(static block => block is not null)
            .Cast<AnalyzedBlock>()
            .Where(static block => !block.IsIgnoredUi)
            .OrderBy(static block => block.Bounds.Top)
            .ThenBy(static block => block.Bounds.Left)
            .ToArray();

        var messages = BuildMessages(analyzedBlocks);
        var fullText = string.Join(
            Environment.NewLine,
            messages
                .Where(static message => !string.IsNullOrWhiteSpace(message.Text))
                .Select(static message => $"{message.Speaker}: {message.Text}"));

        return new WechatConversationParseResult(messages, fullText, bitmap.Width, bitmap.Height, ocrResult.JsonText);
    }

    private static IReadOnlyList<WechatConversationMessage> BuildMessages(IReadOnlyList<AnalyzedBlock> blocks)
    {
        var messages = new List<WechatConversationMessage>();
        var pendingNicknames = new Dictionary<WechatChatSide, AnalyzedBlock>();
        MessageBuilder? current = null;

        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            var next = i + 1 < blocks.Count ? blocks[i + 1] : null;

            if (block.IsTime)
            {
                FlushCurrent(messages, ref current);
                messages.Add(CreateTimeMessage(block));
                continue;
            }

            if (block.IsSystem)
            {
                FlushCurrent(messages, ref current);
                messages.Add(CreateSystemMessage(block));
                continue;
            }

            if (IsNicknameCandidate(block, next))
            {
                FlushCurrent(messages, ref current);
                pendingNicknames[block.Side] = block;
                continue;
            }

            var speaker = ResolveSpeaker(block, pendingNicknames);
            if (ShouldMerge(current, block, speaker))
            {
                current!.Append(block);
                continue;
            }

            FlushCurrent(messages, ref current);
            current = new MessageBuilder(block, speaker);
        }

        FlushCurrent(messages, ref current);
        return messages;
    }

    private static void FlushCurrent(ICollection<WechatConversationMessage> messages, ref MessageBuilder? current)
    {
        if (current is null)
        {
            return;
        }

        messages.Add(current.Build());
        current = null;
    }

    private static bool ShouldMerge(MessageBuilder? current, AnalyzedBlock block, string speaker)
    {
        if (current is null || current.Side != block.Side)
        {
            return false;
        }

        var verticalGap = block.Bounds.Top - current.Bounds.Bottom;
        if (string.Equals(current.Speaker, speaker, StringComparison.Ordinal) &&
            verticalGap <= SameMessageGap)
        {
            return true;
        }

        if (string.Equals(current.Speaker, speaker, StringComparison.Ordinal) &&
            (current.HasMixedBackground || block.BackgroundKind == WechatBubbleColor.Mixed) &&
            verticalGap <= MediaMessageGap)
        {
            return true;
        }

        if (current.HasMixedBackground &&
            block.BackgroundKind == WechatBubbleColor.Mixed &&
            verticalGap <= MediaMessageGap)
        {
            return true;
        }

        return verticalGap <= SameMessageGap &&
               string.Equals(current.Speaker, speaker, StringComparison.Ordinal);
    }

    private static string ResolveSpeaker(AnalyzedBlock block, IDictionary<WechatChatSide, AnalyzedBlock> pendingNicknames)
    {
        if (pendingNicknames.TryGetValue(block.Side, out var nickname) &&
            block.Bounds.Top - nickname.Bounds.Bottom <=
            (block.BackgroundKind == WechatBubbleColor.Mixed ? MediaMessageGap : NicknameAttachGap))
        {
            return NormalizeNicknameText(nickname.Text);
        }

        return block.Side switch
        {
            WechatChatSide.Right => "我",
            WechatChatSide.Left => "对方",
            _ => "系统"
        };
    }

    private static bool IsNicknameCandidate(AnalyzedBlock block, AnalyzedBlock? next)
    {
        if (block.Side == WechatChatSide.Center ||
            block.BackgroundKind is WechatBubbleColor.Green or WechatBubbleColor.White)
        {
            return false;
        }

        if (TimeRegex().IsMatch(block.Text) || UnreadRegex().IsMatch(block.Text))
        {
            return false;
        }

        if (block.Bounds.Height > 38 || block.Text.Length > 40)
        {
            return false;
        }

        if (next is null || next.IsSystem || next.Side != block.Side)
        {
            return false;
        }

        var verticalGap = next.Bounds.Top - block.Bounds.Bottom;
        if (verticalGap < 0 || verticalGap > NicknameAttachGap)
        {
            return false;
        }

        if (MathF.Abs(next.Bounds.Left - block.Bounds.Left) > 42)
        {
            return false;
        }

        var normalized = NormalizeNicknameText(block.Text);
        return normalized.Length is >= 1 and <= 28 &&
               !NicknameNumericRegex().IsMatch(normalized);
    }

    private static WechatConversationMessage CreateSystemMessage(AnalyzedBlock block)
    {
        return new WechatConversationMessage("系统", block.Text, WechatChatSide.Center, WechatMessageKind.System, block.BackgroundKind, block.Bounds, block.Score);
    }

    private static WechatConversationMessage CreateTimeMessage(AnalyzedBlock block)
    {
        return new WechatConversationMessage("时间", block.Text, WechatChatSide.Center, WechatMessageKind.Time, block.BackgroundKind, block.Bounds, block.Score);
    }

    private static AnalyzedBlock? AnalyzeBlock(Bitmap bitmap, OcrBlock block, int index, WechatParseOptions? options)
    {
        if (string.IsNullOrWhiteSpace(block.Text) || block.Score < MinimumUsefulScore)
        {
            return null;
        }

        var bounds = new WechatRect(block.Left, block.Top, block.Right, block.Bottom);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return null;
        }

        var background = SampleBackground(bitmap, bounds, options, block.ColorSample);
        var side = DetermineSide(bitmap.Width, bounds, background.Kind);
        var isTime = IsCenterTimeBlock(block.Text, bounds, bitmap.Width);
        var isSystem = !isTime && IsSystemBlock(block.Text, side, bounds, bitmap.Width);
        var isIgnoredUi = IsIgnoredUiBlock(block.Text, bounds, background.Kind, bitmap.Width, bitmap.Height);

        return new AnalyzedBlock(index, block.Text.Trim(), block.Score, bounds, side, background.Kind, isTime, isSystem, isIgnoredUi);
    }

    private static bool IsCenterTimeBlock(string text, WechatRect bounds, int imageWidth)
    {
        if (!TimeRegex().IsMatch(text))
        {
            return false;
        }

        var centerX = bounds.Left + (bounds.Width / 2f);
        return MathF.Abs(centerX - (imageWidth / 2f)) <= imageWidth * 0.16f;
    }

    private static bool IsSystemBlock(string text, WechatChatSide side, WechatRect bounds, int imageWidth)
    {
        var centerX = bounds.Left + (bounds.Width / 2f);
        if (UnreadRegex().IsMatch(text))
        {
            return true;
        }

        return side == WechatChatSide.Center &&
               MathF.Abs(centerX - (imageWidth / 2f)) <= imageWidth * 0.12f &&
               bounds.Height <= 30;
    }

    private static bool IsIgnoredUiBlock(string text, WechatRect bounds, WechatBubbleColor backgroundKind, int imageWidth, int imageHeight)
    {
        if (bounds.Top <= 28 && bounds.Width >= imageWidth * 0.18f)
        {
            return true;
        }

        if (bounds.Top > imageHeight * 0.16f)
        {
            return false;
        }

        return backgroundKind is WechatBubbleColor.LightGray or WechatBubbleColor.White &&
               (text.Contains('：') || text.Contains(':')) &&
               bounds.Width >= imageWidth * 0.18f;
    }

    private static WechatChatSide DetermineSide(int imageWidth, WechatRect bounds, WechatBubbleColor backgroundKind)
    {
        if (backgroundKind == WechatBubbleColor.Green)
        {
            return WechatChatSide.Right;
        }

        var centerX = bounds.Left + (bounds.Width / 2f);
        if (centerX <= imageWidth * LeftThresholdRatio)
        {
            return WechatChatSide.Left;
        }

        if (centerX >= imageWidth * RightThresholdRatio)
        {
            return WechatChatSide.Right;
        }

        return WechatChatSide.Center;
    }

    private static BackgroundSample SampleBackground(Bitmap bitmap, WechatRect bounds, WechatParseOptions? options, OcrColorSample? preSample)
    {
        if (preSample is not null)
        {
            var sampled = Color.FromArgb(
                (int)Math.Clamp(Math.Round(preSample.Red), 0, 255),
                (int)Math.Clamp(Math.Round(preSample.Green), 0, 255),
                (int)Math.Clamp(Math.Round(preSample.Blue), 0, 255));

            var byConfig = TryClassifyByConfiguredColors(sampled, options);
            if (byConfig is not null)
            {
                return new BackgroundSample(byConfig.Value);
            }

            return new BackgroundSample(
                ClassifyBackgroundColor(preSample.Red, preSample.Green, preSample.Blue, preSample.Variance));
        }

        var colors = new List<Color>(128);
        var left = Math.Max(0, (int)MathF.Floor(bounds.Left));
        var top = Math.Max(0, (int)MathF.Floor(bounds.Top));
        var right = Math.Min(bitmap.Width - 1, (int)MathF.Ceiling(bounds.Right));
        var bottom = Math.Min(bitmap.Height - 1, (int)MathF.Ceiling(bounds.Bottom));

        AddHorizontalBand(bitmap, colors, left, right, Math.Max(0, top - DefaultMarginY), Math.Max(0, top - 1));
        AddHorizontalBand(bitmap, colors, left, right, Math.Min(bitmap.Height - 1, bottom + 1), Math.Min(bitmap.Height - 1, bottom + DefaultMarginY));
        AddVerticalBand(bitmap, colors, Math.Max(0, left - DefaultMarginX), Math.Max(0, left - 1), top, bottom);
        AddVerticalBand(bitmap, colors, Math.Min(bitmap.Width - 1, right + 1), Math.Min(bitmap.Width - 1, right + DefaultMarginX), top, bottom);

        if (colors.Count == 0)
        {
            return new BackgroundSample(WechatBubbleColor.Unknown);
        }

        var averageRed = colors.Average(static color => color.R);
        var averageGreen = colors.Average(static color => color.G);
        var averageBlue = colors.Average(static color => color.B);
        var variance = colors.Average(color =>
        {
            var dr = color.R - averageRed;
            var dg = color.G - averageGreen;
            var db = color.B - averageBlue;
            return dr * dr + dg * dg + db * db;
        });

        var averageColor = Color.FromArgb(
            (int)Math.Clamp(Math.Round(averageRed), 0, 255),
            (int)Math.Clamp(Math.Round(averageGreen), 0, 255),
            (int)Math.Clamp(Math.Round(averageBlue), 0, 255));

        var kind = TryClassifyByConfiguredColors(averageColor, options) ??
                   ClassifyBackgroundColor(averageRed, averageGreen, averageBlue, variance);
        return new BackgroundSample(kind);
    }

    private static WechatBubbleColor? TryClassifyByConfiguredColors(Color sampled, WechatParseOptions? options)
    {
        if (options is null)
        {
            return null;
        }

        if (!TryParseHexColor(options.MyBubbleColorHex, out var myColor) ||
            !TryParseHexColor(options.OpponentBubbleColorHex, out var opponentColor) ||
            !TryParseHexColor(options.ChatBackgroundColorHex, out var chatBackgroundColor))
        {
            return null;
        }

        var myDistance = ColorDistance(sampled, myColor);
        var opponentDistance = ColorDistance(sampled, opponentColor);
        var backgroundDistance = ColorDistance(sampled, chatBackgroundColor);
        var min = Math.Min(myDistance, Math.Min(opponentDistance, backgroundDistance));
        var second = new[] { myDistance, opponentDistance, backgroundDistance }.OrderBy(static distance => distance).Skip(1).First();

        if (min > 70 || (second - min) < 8)
        {
            return null;
        }

        if (Math.Abs(min - myDistance) < 0.001)
        {
            return WechatBubbleColor.Green;
        }

        if (Math.Abs(min - opponentDistance) < 0.001)
        {
            return WechatBubbleColor.White;
        }

        return WechatBubbleColor.Mixed;
    }

    private static bool TryParseHexColor(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var raw = hex.Trim();
        if (raw.StartsWith('#'))
        {
            raw = raw[1..];
        }

        if (raw.Length == 3)
        {
            raw = string.Concat(raw.Select(static c => $"{c}{c}"));
        }

        if (raw.Length != 6)
        {
            return false;
        }

        if (!int.TryParse(raw.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber, null, out var r) ||
            !int.TryParse(raw.AsSpan(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g) ||
            !int.TryParse(raw.AsSpan(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            return false;
        }

        color = Color.FromArgb(r, g, b);
        return true;
    }

    private static double ColorDistance(Color a, Color b)
    {
        var dr = a.R - b.R;
        var dg = a.G - b.G;
        var db = a.B - b.B;
        return Math.Sqrt((dr * dr) + (dg * dg) + (db * db));
    }

    private static WechatBubbleColor ClassifyBackgroundColor(double red, double green, double blue, double variance)
    {
        var brightness = (red + green + blue) / 3d;
        var channelRange = Math.Max(red, Math.Max(green, blue)) - Math.Min(red, Math.Min(green, blue));

        if (variance > 1500)
        {
            return WechatBubbleColor.Mixed;
        }

        if (green > red + 10 && green > blue + 10 && brightness > 170)
        {
            return WechatBubbleColor.Green;
        }

        if (brightness >= 246)
        {
            return WechatBubbleColor.White;
        }

        if (brightness >= 216 && channelRange <= 18)
        {
            return WechatBubbleColor.LightGray;
        }

        if (brightness >= 185 && channelRange <= 25)
        {
            return WechatBubbleColor.Gray;
        }

        return WechatBubbleColor.Mixed;
    }

    private static void AddHorizontalBand(Bitmap bitmap, ICollection<Color> colors, int left, int right, int top, int bottom)
    {
        if (top > bottom)
        {
            return;
        }

        for (var y = top; y <= bottom; y++)
        {
            for (var x = left; x <= right; x += 3)
            {
                colors.Add(bitmap.GetPixel(x, y));
            }
        }
    }

    private static void AddVerticalBand(Bitmap bitmap, ICollection<Color> colors, int left, int right, int top, int bottom)
    {
        if (left > right)
        {
            return;
        }

        for (var x = left; x <= right; x++)
        {
            for (var y = top; y <= bottom; y += 3)
            {
                colors.Add(bitmap.GetPixel(x, y));
            }
        }
    }

    [GeneratedRegex(@"^\d{1,2}:\d{2}([:：]\d{2})?$", RegexOptions.Compiled)]
    private static partial Regex TimeRegex();

    [GeneratedRegex(@"^\d+条新消息$", RegexOptions.Compiled)]
    private static partial Regex UnreadRegex();

    [GeneratedRegex(@"^\d{1,20}$", RegexOptions.Compiled)]
    private static partial Regex NicknameNumericRegex();

    private sealed record BackgroundSample(WechatBubbleColor Kind);

    private sealed record AnalyzedBlock(
        int Index,
        string Text,
        float Score,
        WechatRect Bounds,
        WechatChatSide Side,
        WechatBubbleColor BackgroundKind,
        bool IsTime,
        bool IsSystem,
        bool IsIgnoredUi);

    private static string NormalizeNicknameText(string raw)
    {
        return raw.Trim().TrimEnd(':', '：');
    }

    private sealed class MessageBuilder
    {
        private readonly List<AnalyzedBlock> _blocks;

        public MessageBuilder(AnalyzedBlock first, string speaker)
        {
            _blocks = new List<AnalyzedBlock> { first };
            Speaker = speaker;
            Side = first.Side;
            Bounds = first.Bounds;
        }

        public string Speaker { get; }

        public WechatChatSide Side { get; }

        public WechatRect Bounds { get; private set; }

        public bool HasMixedBackground => _blocks.Any(static block => block.BackgroundKind == WechatBubbleColor.Mixed);

        public void Append(AnalyzedBlock block)
        {
            _blocks.Add(block);
            Bounds = Bounds.Union(block.Bounds);
        }

        public WechatConversationMessage Build()
        {
            var text = string.Join(Environment.NewLine, _blocks.Select(static block => block.Text));
            var bubble = ResolveBubbleColor(_blocks);
            var confidence = _blocks.Average(static block => block.Score);
            var kind = DetermineMessageKind(_blocks, bubble);
            return new WechatConversationMessage(Speaker, text, Side, kind, bubble, Bounds, confidence);
        }

        private static WechatBubbleColor ResolveBubbleColor(IEnumerable<AnalyzedBlock> blocks)
        {
            var ranked = blocks
                .GroupBy(static block => block.BackgroundKind)
                .OrderByDescending(static group => group.Count())
                .ThenBy(static group => group.Key == WechatBubbleColor.Mixed ? 1 : 0)
                .FirstOrDefault();

            return ranked?.Key ?? WechatBubbleColor.Unknown;
        }

        private static WechatMessageKind DetermineMessageKind(IEnumerable<AnalyzedBlock> blocks, WechatBubbleColor bubble)
        {
            if (bubble == WechatBubbleColor.Mixed || blocks.Any(static block => block.Bounds.Height > 80))
            {
                return WechatMessageKind.Media;
            }

            return WechatMessageKind.Text;
        }
    }
}

internal sealed record WechatConversationParseResult(
    IReadOnlyList<WechatConversationMessage> Messages,
    string FullText,
    int ImageWidth,
    int ImageHeight,
    string RawOcrJson);

internal sealed record WechatConversationMessage(
    string Speaker,
    string Text,
    WechatChatSide Side,
    WechatMessageKind MessageKind,
    WechatBubbleColor BubbleColor,
    WechatRect Bounds,
    float Confidence);

internal enum WechatChatSide
{
    Left,
    Right,
    Center
}

internal enum WechatMessageKind
{
    Time,
    Text,
    Media,
    System
}

internal enum WechatBubbleColor
{
    Unknown,
    White,
    LightGray,
    Gray,
    Green,
    Mixed
}

internal sealed record WechatRect(float Left, float Top, float Right, float Bottom)
{
    public float Width => MathF.Max(0, Right - Left);

    public float Height => MathF.Max(0, Bottom - Top);

    public WechatRect Union(WechatRect other)
    {
        return new WechatRect(
            MathF.Min(Left, other.Left),
            MathF.Min(Top, other.Top),
            MathF.Max(Right, other.Right),
            MathF.Max(Bottom, other.Bottom));
    }
}

internal sealed record WechatParseOptions(
    string? OpponentBubbleColorHex = null,
    string? MyBubbleColorHex = null,
    string? ChatBackgroundColorHex = null);
