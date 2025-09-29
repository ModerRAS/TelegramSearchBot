using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TelegramSearchBot.Search.Model;

namespace TelegramSearchBot.Search.Tool {
    public class FieldSpecificationParser {
        private static readonly Regex FieldRegex = new Regex(@"(\w+):([^\s]+)", RegexOptions.Compiled);
        private readonly Action<string>? _logAction;

        public FieldSpecificationParser(Action<string>? logAction = null) {
            _logAction = logAction;
        }

        public FieldSpec? ParseFieldSpecification(string fieldSpec) {
            if (string.IsNullOrWhiteSpace(fieldSpec)) {
                return null;
            }

            var parts = fieldSpec.Split(':', 2);
            if (parts.Length != 2) {
                return null;
            }

            var fieldName = parts[0].Trim();
            var fieldValue = parts[1].Trim();
            var actualFieldName = ResolveFieldAlias(fieldName);

            return new FieldSpec(actualFieldName, fieldValue);
        }

        public List<FieldSpec> ParseFieldSpecifications(string query) {
            var fieldSpecs = new List<FieldSpec>();
            var matches = FieldRegex.Matches(query);
            foreach (Match match in matches) {
                var fieldSpec = ParseFieldSpecification(match.Value);
                if (fieldSpec != null) {
                    fieldSpecs.Add(fieldSpec);
                }
            }
            return fieldSpecs;
        }

        public (List<FieldSpec> FieldSpecs, string RemainingQuery) ExtractFieldSpecifications(string query) {
            var fieldSpecs = ParseFieldSpecifications(query);
            var remaining = query;

            foreach (var fieldSpec in fieldSpecs) {
                remaining = remaining.Replace($"{fieldSpec.FieldName}:{fieldSpec.FieldValue}", string.Empty);
            }

            return (fieldSpecs, remaining.Trim());
        }

        public bool IsValidFieldSpec(FieldSpec? fieldSpec) {
            if (fieldSpec == null || string.IsNullOrWhiteSpace(fieldSpec.FieldName) || string.IsNullOrWhiteSpace(fieldSpec.FieldValue)) {
                return false;
            }

            if (fieldSpec.FieldName.StartsWith("Ext_", StringComparison.OrdinalIgnoreCase) ||
                fieldSpec.FieldName.Equals("Content", StringComparison.OrdinalIgnoreCase)) {
                return true;
            }

            _logAction?.Invoke($"FieldSpecificationParser: 字段 {fieldSpec.FieldName} 无效");
            return false;
        }

        private string ResolveFieldAlias(string fieldName) {
            return fieldName.ToLowerInvariant() switch {
                "content" => "Content",
                "ocr" => "Ext_OCR_Result",
                "asr" => "Ext_ASR_Result",
                "qr" => "Ext_QR_Result",
                _ => fieldName
            };
        }
    }
}
