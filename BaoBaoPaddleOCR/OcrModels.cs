namespace BaoBaoPaddleOCR;

public sealed record OcrBlock(string Text, float Score);

public sealed record OcrResult(string Text, string JsonText, IReadOnlyList<OcrBlock> Blocks);
