using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Text;
using TelegramSearchBot.Service;

namespace TelegramSearchBot.Tests.Services {
    [TestClass]
    public class AutoInstantViewServiceUnitTest {
        [TestMethod]
        public void TestGetWeChatUrls() {
            var TestMessage = "这是https://mp.weixin.qq.com/s/8VhUUIgS9JWxfc-hEaa8ow 啊https://mp.weixin.qq.com/aa zhesihhttps://mp.weixin.qq.com,ahttps://weixin.qq.com/a2a。啊https://mp.weixin.qq.com/s/8VhUUIgS9JWxfc-hEUW8ow,ahttps://mp.weixin.qq.com/s/8VhUBBgS9JWxfc-hEUW8ow";
            var Output = new HashSet<string>() {
                "https://mp.weixin.qq.com/s/8VhUUIgS9JWxfc-hEaa8ow",
                "https://mp.weixin.qq.com/aa",
                "https://mp.weixin.qq.com/s/8VhUUIgS9JWxfc-hEUW8ow",
                "https://mp.weixin.qq.com/s/8VhUBBgS9JWxfc-hEUW8ow"
            };
            var weChatUrls = AutoInstantViewService.GetWeChatUrls(TestMessage);
            Assert.AreEqual(Output.Count, weChatUrls.Count);
            foreach(var e in weChatUrls) {
                Assert.IsTrue(Output.Contains(e));
            }

        }
    }
}
