using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Markdig;
using Xunit;

namespace TelegramSearchBot.Test.Helper {
    /// <summary>
    /// Tests for Markdig and System.Linq.Async package usage.
    /// These tests validate behavior before dependency upgrades.
    /// </summary>
    public class UtilsTests {
        // --- Markdig: basic Markdown→HTML conversion ---

        [Fact]
        public void Markdig_PlainText_ProducesHtml() {
            var pipeline = new MarkdownPipelineBuilder().Build();
            var html = Markdown.ToHtml("Hello world", pipeline);
            Assert.Contains("Hello world", html);
        }

        [Fact]
        public void Markdig_BoldText_ProducesStrongTag() {
            var pipeline = new MarkdownPipelineBuilder().Build();
            var html = Markdown.ToHtml("**bold**", pipeline);
            Assert.Contains("<strong>bold</strong>", html);
        }

        [Fact]
        public void Markdig_Heading_ProducesH1Tag() {
            var pipeline = new MarkdownPipelineBuilder().Build();
            var html = Markdown.ToHtml("# Heading", pipeline);
            Assert.Contains("<h1>Heading</h1>", html);
        }

        [Fact]
        public void Markdig_Link_ProducesAnchorTag() {
            var pipeline = new MarkdownPipelineBuilder().Build();
            var html = Markdown.ToHtml("[click](https://example.com)", pipeline);
            Assert.Contains("href=\"https://example.com\"", html);
            Assert.Contains("click", html);
        }

        [Fact]
        public void Markdig_EmptyInput_ProducesEmptyOrWhitespaceOnly() {
            var pipeline = new MarkdownPipelineBuilder().Build();
            var html = Markdown.ToHtml("", pipeline);
            Assert.True(string.IsNullOrWhiteSpace(html));
        }

        [Fact]
        public void Markdig_PipelineBuilder_CanBuildWithExtensions() {
            var pipeline = new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .Build();
            var html = Markdown.ToHtml("**bold** and *italic*", pipeline);
            Assert.NotEmpty(html);
        }

        // --- System.Linq.Async: ToListAsync on IAsyncEnumerable ---

        [Fact]
        public async Task ToListAsync_EmptyAsyncSequence_ReturnsEmptyList() {
            var asyncEnum = GetEmptyAsync();
            var result = await asyncEnum.ToListAsync();
            Assert.Empty(result);
        }

        [Fact]
        public async Task ToListAsync_NonEmptyAsyncSequence_ReturnsAllItems() {
            var asyncEnum = GetItemsAsync(1, 2, 3);
            var result = await asyncEnum.ToListAsync();
            Assert.Equal(new[] { 1, 2, 3 }, result);
        }

        [Fact]
        public async Task SelectAwait_AsyncSequence_TransformsItems() {
            var asyncEnum = GetItemsAsync(1, 2, 3);
            var result = await asyncEnum
                .SelectAwait(async x => { await Task.Yield(); return x * 2; })
                .ToListAsync();
            Assert.Equal(new[] { 2, 4, 6 }, result);
        }

        [Fact]
        public async Task WhereAwait_AsyncSequence_FiltersItems() {
            var asyncEnum = GetItemsAsync(1, 2, 3, 4, 5);
            var result = await asyncEnum
                .WhereAwait(async x => { await Task.Yield(); return x % 2 == 0; })
                .ToListAsync();
            Assert.Equal(new[] { 2, 4 }, result);
        }

        [Fact]
        public async Task CountAsync_AsyncSequence_ReturnsCount() {
            var asyncEnum = GetItemsAsync(10, 20, 30);
            var count = await asyncEnum.CountAsync();
            Assert.Equal(3, count);
        }

        [Fact]
        public async Task FirstOrDefaultAsync_AsyncSequence_ReturnsFirst() {
            var asyncEnum = GetItemsAsync(5, 6, 7);
            var first = await asyncEnum.FirstOrDefaultAsync();
            Assert.Equal(5, first);
        }

        private static async IAsyncEnumerable<int> GetEmptyAsync() {
            yield break;
        }

        private static async IAsyncEnumerable<int> GetItemsAsync(params int[] items) {
            foreach (var item in items) {
                await Task.Yield();
                yield return item;
            }
        }
    }
}
