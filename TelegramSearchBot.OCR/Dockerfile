FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["TelegramSearchBot.OCR/TelegramSearchBot.OCR.csproj", "TelegramSearchBot.OCR/"]
COPY ["TelegramSearchBot.Common/TelegramSearchBot.Common.csproj", "TelegramSearchBot.Common/"]
RUN dotnet restore "TelegramSearchBot.OCR/TelegramSearchBot.OCR.csproj"
COPY . .
WORKDIR "/src/TelegramSearchBot.OCR"
RUN dotnet build "TelegramSearchBot.OCR.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "TelegramSearchBot.OCR.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# 安装OCR所需的依赖
RUN apt-get update && apt-get install -y \
    libgdiplus \
    libc6-dev \
    libx11-dev \
    libfontconfig1 \
    && rm -rf /var/lib/apt/lists/*

ENTRYPOINT ["dotnet", "TelegramSearchBot.OCR.dll"]