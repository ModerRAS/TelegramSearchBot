﻿using LiteDB;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using TelegramSearchBot.Intrerface;
using TelegramSearchBot.Model;

namespace TelegramSearchBot.Manager {
    public class TokenManager : ITokenManager {
        private ILiteCollection<TokenModel> liteCollection { get; set; }
        private ConcurrentDictionary<string, HashSet<string>> Tokens {  get; set; }
        private ILogger<TokenManager> logger { get; set; }
        public TokenManager(LiteDbManager liteDbManager, ILogger<TokenManager> logger) {
            this.logger = logger;
            Tokens = new ConcurrentDictionary<string, HashSet<string>>();
            liteCollection = liteDbManager.Cache.GetCollection<TokenModel>("Token");
            var AllToken = liteCollection.FindAll();
            foreach (var token in AllToken) {
                if (Tokens.Keys.Contains(token.Type)) {
                    Tokens[token.Type].Add(token.Token);
                } else {
                    Tokens.TryAdd(token.Type, new HashSet<string>() { token.Token });
                }
            }
        }
        public List<string> ListToken(string TokenType) {
            if (Tokens.TryGetValue(TokenType, out var list)) {
                logger.LogInformation($"ListToken {TokenType}");
                return list.ToList();
            } else {
                logger.LogInformation($"ListToken {TokenType} Empty");
                return new List<string>();
            }
             
        }
        public bool RemoveToken(string TokenType, string Token) {
            logger.LogInformation($"Try remove Token {Token} {TokenType}");
            if (Tokens.TryGetValue(TokenType, out var list)) {
                list.Remove(Token);
                var tokens = liteCollection.DeleteMany(token => token.Token.Equals(Token) && token.Type.Equals(TokenType));
                return true;
            }
            return false;
        }
        public void AddToken(string TokenType, string Token) {
            logger.LogInformation($"Try add Token {Token} {TokenType}");
            if (!Tokens.ContainsKey(TokenType)) {
                Tokens.TryAdd(TokenType, new HashSet<string>() { Token });
            } else {
                Tokens[TokenType].Add(Token);
            }
            liteCollection.Insert(new TokenModel() { Type = TokenType, Token = Token });
        }
        public bool CheckToken(string TokenType, string Token) {
            logger.LogInformation($"Check Token {Token} {TokenType}");
            if (!Tokens.ContainsKey(TokenType)) {
                return false;
            }
            if (Tokens[TokenType].Contains(Token)) {
                return true;
            }
            return false;
        }
    }
}