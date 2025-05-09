using System;
using System.Threading.Tasks;
using TelegramSearchBot.Service.Common;

namespace TelegramSearchBot.Test.Service.Common
{
    [TestClass]
    public class ChatContextProviderTests
    {
        [TestCleanup]
        public void TestCleanup()
        {
            // Ensure the context is cleared after each test to prevent interference
            ChatContextProvider.Clear();
        }

        [TestMethod]
        public void SetCurrentChatId_And_GetCurrentChatId_ReturnsSetValue()
        {
            long expectedChatId = 12345L;
            ChatContextProvider.SetCurrentChatId(expectedChatId);
            long actualChatId = ChatContextProvider.GetCurrentChatId();
            Assert.AreEqual(expectedChatId, actualChatId);
        }

        [TestMethod]
        public async Task SetCurrentChatId_InAsyncTask_GetCurrentChatId_ReturnsSetValueInSameContext()
        {
            long expectedChatId = 67890L;

            await Task.Run(() =>
            {
                ChatContextProvider.SetCurrentChatId(expectedChatId);
                long actualChatId = ChatContextProvider.GetCurrentChatId();
                Assert.AreEqual(expectedChatId, actualChatId, "ChatId should be available in the same async flow.");

                // Test nested async call
                Func<Task> nestedCall = async () =>
                {
                    await Task.Yield(); // Ensure it's on a potentially different thread but same async context
                    long nestedActualChatId = ChatContextProvider.GetCurrentChatId();
                    Assert.AreEqual(expectedChatId, nestedActualChatId, "ChatId should be available in nested async calls within the same context.");
                };
                nestedCall().Wait(); // Using .Wait() for simplicity in test, ensure it completes.
            });
        }

        [TestMethod]
        public void GetCurrentChatId_WhenNotSet_And_ThrowIfNotFoundTrue_ThrowsInvalidOperationException()
        {
            // Ensure it's cleared from any previous test
            ChatContextProvider.Clear(); 
            Assert.ThrowsException<InvalidOperationException>(() => ChatContextProvider.GetCurrentChatId(true));
        }

        [TestMethod]
        public void GetCurrentChatId_WhenNotSet_And_ThrowIfNotFoundFalse_ReturnsDefaultLong()
        {
            // Ensure it's cleared
            ChatContextProvider.Clear();
            long actualChatId = ChatContextProvider.GetCurrentChatId(false);
            Assert.AreEqual(0L, actualChatId, "Should return default(long) which is 0 when not set and throwIfNotFound is false.");
        }

        [TestMethod]
        public void Clear_RemovesChatIdFromContext()
        {
            long chatId = 98765L;
            ChatContextProvider.SetCurrentChatId(chatId);
            Assert.AreEqual(chatId, ChatContextProvider.GetCurrentChatId(), "ChatId should be set initially.");

            ChatContextProvider.Clear();

            // After clearing, getting with throwIfNotFound=true should throw
            Assert.ThrowsException<InvalidOperationException>(() => ChatContextProvider.GetCurrentChatId(true), 
                "Should throw after Clear() when throwIfNotFound is true.");
            
            // And getting with throwIfNotFound=false should return default
            Assert.AreEqual(0L, ChatContextProvider.GetCurrentChatId(false), 
                "Should return default(long) after Clear() when throwIfNotFound is false.");
        }

        [TestMethod]
        public async Task ChatId_IsNotSharedAcrossDifferentAsyncFlows_UnlessFlowed()
        {
            long firstFlowChatId = 111L;
            long secondFlowChatId = 222L;

            Task firstTask = Task.Run(() =>
            {
                ChatContextProvider.SetCurrentChatId(firstFlowChatId);
                Assert.AreEqual(firstFlowChatId, ChatContextProvider.GetCurrentChatId());
                // Keep this context for a bit to overlap with the second task potentially
                Task.Delay(50).Wait(); 
                Assert.AreEqual(firstFlowChatId, ChatContextProvider.GetCurrentChatId(), "ChatId in first flow should remain unchanged.");
            });

            Task secondTask = Task.Run(() =>
            {
                // This flow should not see the ChatId from the first flow
                Assert.ThrowsException<InvalidOperationException>(() => ChatContextProvider.GetCurrentChatId(true), 
                    "Second flow should not see ChatId from the first flow initially.");
                
                ChatContextProvider.SetCurrentChatId(secondFlowChatId);
                Assert.AreEqual(secondFlowChatId, ChatContextProvider.GetCurrentChatId());
                Task.Delay(50).Wait();
                Assert.AreEqual(secondFlowChatId, ChatContextProvider.GetCurrentChatId(), "ChatId in second flow should remain unchanged.");

            });

            await Task.WhenAll(firstTask, secondTask);

            // After both tasks complete, the main test context should still be clear (or whatever it was before)
            // Since TestCleanup calls Clear(), it should be clear here.
            Assert.ThrowsException<InvalidOperationException>(() => ChatContextProvider.GetCurrentChatId(true), 
                "Main test context should be clear after parallel tasks complete.");
        }
    }
}
