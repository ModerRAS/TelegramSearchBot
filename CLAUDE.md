# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview
TelegramSearchBot is a .NET 9.0 console application that provides Telegram bot functionality for group chat message storage, search, and AI processing. It supports traditional keyword search via Lucene.NET and semantic search via FAISS vectors.

## Architecture
- **Message Processing Pipeline**: MediatR-based event handling with async message processing
- **Storage**: SQLite + EF Core 9.0 for data, Lucene.NET for full-text search, FAISS for vectors
- **AI Services**: OCR (PaddleOCR), ASR (Whisper), LLM (Ollama/OpenAI/Gemini)
- **Multi-Modality**: Handles text, images, audio, video with automatic content extraction
- **Background Tasks**: Coravel scheduler for periodic indexing and processing

## Build Commands
```bash
# Restore dependencies
dotnet restore TelegramSearchBot.sln

# Build solution
dotnet build TelegramSearchBot.sln --configuration Release

# Run tests
dotnet test

# Run specific test category
dotnet test --filter "Category=Vector"

# Publish for Windows (current target)
dotnet publish -r win-x64 --self-contained
```

## Key Configuration
- **Config Location**: `%LOCALAPPDATA%/TelegramSearchBot/Config.json`
- **Required**: `BotToken`, `AdminId`
- **AI Models**: Configurable via `OllamaModelName`, `OpenAIModelName`
- **Features**: `EnableAutoOCR`, `EnableAutoASR`, `EnableVideoASR`

## Development Notes
- **Platform**: Currently Windows-only due to runtime identifiers and native dependencies
- **Tests**: xUnit with Moq, EF Core InMemory for database testing
- **Logging**: Serilog with console, file, and OpenTelemetry sinks
- **Database**: EF Core migrations for SQLite schema management

## Important Paths
- **Main Entry**: `TelegramSearchBot/Program.cs`
- **Configuration**: `TelegramSearchBot/Env.cs`
- **Controllers**: `TelegramSearchBot/Controller/` - Handle bot commands
- **Services**: `TelegramSearchBot/Service/` - Core business logic
- **Models**: `TelegramSearchBot/Model/` - EF entities and DTOs
- **Tests**: `TelegramSearchBot.Test/` - xUnit tests