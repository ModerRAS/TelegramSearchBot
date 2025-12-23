//! 中文分词器
//!
//! 使用 jieba-rs 进行中文分词

use jieba_rs::Jieba;
use once_cell::sync::Lazy;
use tantivy::tokenizer::{Token, TokenStream, Tokenizer};

/// 全局结巴分词实例
static JIEBA: Lazy<Jieba> = Lazy::new(Jieba::new);

/// 中文分词器
#[derive(Clone)]
pub struct ChineseTokenizer;

impl Tokenizer for ChineseTokenizer {
    type TokenStream<'a> = ChineseTokenStream;

    fn token_stream<'a>(&'a mut self, text: &'a str) -> Self::TokenStream<'a> {
        ChineseTokenStream::new(text)
    }
}

/// 中文分词流
pub struct ChineseTokenStream {
    tokens: Vec<Token>,
    index: usize,
}

impl ChineseTokenStream {
    pub fn new(text: &str) -> Self {
        let words = JIEBA.cut(text, true);
        let mut tokens = Vec::new();
        let mut offset = 0;

        for word in words {
            let word_trimmed = word.trim();
            if word_trimmed.is_empty() {
                offset += word.len();
                continue;
            }

            // 跳过纯标点符号
            if word_trimmed.chars().all(|c| c.is_ascii_punctuation() || is_chinese_punctuation(c)) {
                offset += word.len();
                continue;
            }

            tokens.push(Token {
                offset_from: offset,
                offset_to: offset + word.len(),
                position: tokens.len(),
                text: word_trimmed.to_lowercase(),
                position_length: 1,
            });

            offset += word.len();
        }

        Self { tokens, index: 0 }
    }
}

impl TokenStream for ChineseTokenStream {
    fn advance(&mut self) -> bool {
        if self.index < self.tokens.len() {
            self.index += 1;
            true
        } else {
            false
        }
    }

    fn token(&self) -> &Token {
        &self.tokens[self.index - 1]
    }

    fn token_mut(&mut self) -> &mut Token {
        &mut self.tokens[self.index - 1]
    }
}

/// 判断是否是中文标点
fn is_chinese_punctuation(c: char) -> bool {
    matches!(c, 
        '，' | '。' | '！' | '？' | '、' | '；' | '：' | 
        '\u{201C}' | '\u{201D}' |  // 中文双引号 ""
        '\u{2018}' | '\u{2019}' |  // 中文单引号 ''
        '（' | '）' | '【' | '】' | '《' | '》' | 
        '—' | '…' | '·'
    )
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_chinese_tokenizer() {
        let mut tokenizer = ChineseTokenizer;
        let mut stream = tokenizer.token_stream("你好世界");

        let mut tokens = Vec::new();
        while stream.advance() {
            tokens.push(stream.token().text.clone());
        }

        assert!(!tokens.is_empty());
    }

    #[test]
    fn test_mixed_text() {
        let mut tokenizer = ChineseTokenizer;
        let mut stream = tokenizer.token_stream("Hello 世界 Test123");

        let mut tokens = Vec::new();
        while stream.advance() {
            tokens.push(stream.token().text.clone());
        }

        assert!(!tokens.is_empty());
    }
}
