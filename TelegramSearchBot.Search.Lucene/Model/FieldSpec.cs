using System;

namespace TelegramSearchBot.Search.Lucene.Model {
    /// <summary>
    /// 字段规范数据模型，来源于 LuceneManager 中的嵌套类
    /// </summary>
    public class FieldSpec {
        public string FieldName { get; set; } = string.Empty;
        public string FieldValue { get; set; } = string.Empty;

        public bool IsExtField => FieldName != null && FieldName.StartsWith("Ext_");
        public bool IsContentField => string.Equals(FieldName, "Content", StringComparison.OrdinalIgnoreCase);

        public FieldSpec() { }

        public FieldSpec(string fieldName, string fieldValue) {
            FieldName = fieldName;
            FieldValue = fieldValue;
        }

        public override string ToString() {
            return $"{FieldName}:{FieldValue}";
        }
    }
}
