using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.AI;

namespace TelegramSearchBot.Service.Manage {
    public class EditLLMConfService : IService {
        public string ServiceName => "EditLLMConfService";
        protected readonly DataDbContext DataContext;
        public EditLLMConfService(DataDbContext context) {
            DataContext = context;
        }

        /// <summary>
        /// 添加一个新的LLM通道到数据库
        /// </summary>
        /// <param name="Name"></param>
        /// <param name="Gateway"></param>
        /// <param name="ApiKey"></param>
        /// <param name="Provider"></param>
        /// <returns></returns>
        public async Task<bool> AddChannel(string Name, string Gateway, string ApiKey, LLMProvider Provider) {
            

        }

        public async Task AddModelWithChannel() {

        }
    }
}
