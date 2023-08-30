using LiteDB;

namespace TelegramSearchBot.Manager {
    public class LiteDbManager {
        public LiteDbManager() {
            //Env.Database = new LiteDatabase($"{Env.WorkDir}/Data.db");
            //Env.Cache = new LiteDatabase($"{Env.WorkDir}/Cache.db");
        }
        public LiteDatabase Database { get => Env.Database; }
        public LiteDatabase Cache { get => Env.Cache; }
    }
}
