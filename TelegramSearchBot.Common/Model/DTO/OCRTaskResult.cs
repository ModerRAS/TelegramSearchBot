using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Common.Interface;
using TelegramSearchBot.Common.Model.DO;

namespace TelegramSearchBot.Common.Model.DTO {
    [Obsolete]
    public class OCRTaskResult : ICompareRPC {
        public Guid Id { get; set; }
        public PaddleOCRResult PaddleOCRResult { get; set; }
        public string GetUniqueId() {
            return Id.ToString();
        }
    }
}
