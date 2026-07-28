using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace TelegramSearchBot.Service.AI.OCR {
    internal readonly record struct OcrImageSlice(int Top, int Height, bool HasOverlap);

    internal static class LongImageOcrSlicer {
        internal const int LongImageHeightThreshold = 4096;
        internal const float MinimumLongImageAspectRatio = 2.5f;
        internal const int TargetSliceHeight = 3072;
        internal const int CutSearchRadius = 384;
        internal const int FallbackOverlap = 96;

        private const int AnalysisWidth = 256;
        private const int MinimumSliceHeight = 1024;
        private const int BrightnessDifferenceThreshold = 24;
        private const int CutDensityRadius = 3;
        private const double SafeRowDensityThreshold = 0.025;

        internal static IReadOnlyList<OcrImageSlice> PlanSlices(SKBitmap image) {
            if (image.Height <= LongImageHeightThreshold ||
                image.Height / ( float ) image.Width < MinimumLongImageAspectRatio) {
                return [new OcrImageSlice(0, image.Height, false)];
            }

            var rowDensities = CalculateRowDensities(image);
            var slices = new List<OcrImageSlice>();
            var top = 0;

            while (image.Height - top > LongImageHeightThreshold) {
                var targetBottom = Math.Min(top + TargetSliceHeight, image.Height);
                var searchStart = Math.Max(top + MinimumSliceHeight, targetBottom - CutSearchRadius);
                var searchEnd = Math.Min(image.Height - 1, targetBottom + CutSearchRadius);
                var cut = FindBestCut(rowDensities, searchStart, searchEnd);
                var safeCut = AverageDensity(rowDensities, cut, CutDensityRadius) <= SafeRowDensityThreshold;
                var hasOverlap = slices.Count > 0 && slices[^1].Top + slices[^1].Height > top;

                slices.Add(new OcrImageSlice(top, cut - top, hasOverlap));
                top = safeCut ? cut : Math.Max(top + 1, cut - FallbackOverlap);
            }

            slices.Add(new OcrImageSlice(top, image.Height - top, top > 0 && slices[^1].Top + slices[^1].Height > top));
            return slices;
        }

        internal static string MergeResults(IEnumerable<(OcrImageSlice Slice, string Text)> results) {
            var mergedTokens = new List<string>();
            var previousSliceHadText = false;

            foreach (var (slice, text) in results) {
                if (string.IsNullOrWhiteSpace(text)) {
                    previousSliceHadText = false;
                    continue;
                }

                var nextTokens = SplitTokens(text);
                var duplicateTokenCount = slice.HasOverlap && previousSliceHadText
                    ? FindDuplicateBoundary(mergedTokens, nextTokens)
                    : 0;
                mergedTokens.AddRange(nextTokens.Skip(duplicateTokenCount));
                previousSliceHadText = true;
            }

            return string.Join(' ', mergedTokens);
        }

        private static double[] CalculateRowDensities(SKBitmap image) {
            var sampleStep = Math.Max(1, image.Width / AnalysisWidth);
            var sampledColumns = ( image.Width + sampleStep - 1 ) / sampleStep;
            var densities = new double[image.Height];

            for (var y = 0; y < image.Height; y++) {
                long red = 0;
                long green = 0;
                long blue = 0;
                for (var x = 0; x < image.Width; x += sampleStep) {
                    var pixel = image.GetPixel(x, y);
                    red += pixel.Red;
                    green += pixel.Green;
                    blue += pixel.Blue;
                }

                var averageRed = red / sampledColumns;
                var averageGreen = green / sampledColumns;
                var averageBlue = blue / sampledColumns;
                var contentPixels = 0;
                for (var x = 0; x < image.Width; x += sampleStep) {
                    var pixel = image.GetPixel(x, y);
                    if (Math.Abs(pixel.Red - averageRed) > BrightnessDifferenceThreshold ||
                        Math.Abs(pixel.Green - averageGreen) > BrightnessDifferenceThreshold ||
                        Math.Abs(pixel.Blue - averageBlue) > BrightnessDifferenceThreshold) {
                        contentPixels++;
                    }
                }

                densities[y] = contentPixels / ( double ) sampledColumns;
            }

            return densities;
        }

        private static int FindBestCut(double[] rowDensities, int searchStart, int searchEnd) {
            var target = ( searchStart + searchEnd ) / 2;
            var bestCut = target;
            var bestScore = double.MaxValue;

            for (var y = searchStart; y <= searchEnd; y++) {
                var density = AverageDensity(rowDensities, y, CutDensityRadius);
                var distancePenalty = Math.Abs(y - target) / ( double ) Math.Max(1, searchEnd - searchStart) * 0.01;
                var score = density + distancePenalty;
                if (score < bestScore) {
                    bestScore = score;
                    bestCut = y;
                }
            }

            return bestCut;
        }

        private static double AverageDensity(double[] rowDensities, int center, int radius) {
            var start = Math.Max(0, center - radius);
            var end = Math.Min(rowDensities.Length - 1, center + radius);
            var total = 0d;
            for (var y = start; y <= end; y++) {
                total += rowDensities[y];
            }
            return total / ( end - start + 1 );
        }

        private static List<string> SplitTokens(string value) {
            return value
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split(( char[]? ) null, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .ToList();
        }

        private static int FindDuplicateBoundary(IReadOnlyList<string> mergedLines, IReadOnlyList<string> nextLines) {
            var maximumOverlap = Math.Min(Math.Min(mergedLines.Count, nextLines.Count), 50);
            for (var overlap = maximumOverlap; overlap > 0; overlap--) {
                var matches = true;
                for (var index = 0; index < overlap; index++) {
                    if (!string.Equals(
                        mergedLines[mergedLines.Count - overlap + index],
                        nextLines[index],
                        StringComparison.Ordinal)) {
                        matches = false;
                        break;
                    }
                }

                if (matches) {
                    return overlap;
                }
            }

            return 0;
        }
    }
}
