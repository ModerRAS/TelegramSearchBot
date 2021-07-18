//using System;
//using System.Collections.Generic;
//using System.Text;
//using Telegram.Bot;
//using Telegram.Bot.Args;
//using TelegramSearchBot.Intrerface;

//namespace TelegramSearchBot.Controller {
//    class InlineSearch : IOnInlineQuery {
//        private readonly ISearchService searchService;
//        private readonly SendService sendService;
//        public InlineSearch(
//            ITelegramBotClient botClient,
//            ISearchService searchService,
//            SendService sendService
//            ) : base(botClient) {
//            this.searchService = searchService;
//            this.sendService = sendService;
//        }
//        protected override void ExecuteAsync(object sender, InlineQueryEventArgs e) {
            
//            throw new NotImplementedException();
//        }
//    }
//}
