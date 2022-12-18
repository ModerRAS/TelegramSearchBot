FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build-env
WORKDIR /app

# Copy everything else and build
COPY . ./
RUN dotnet publish ./TelegramSearchBot/TelegramSearchBot.csproj -c Release -o /app/out -r linux-x64 --self-contained false


FROM mcr.microsoft.com/dotnet/runtime:7.0

RUN apt update -y && \
    apt install -y fontconfig && \
    apt-get install -y --allow-unauthenticated \
        libc6-dev \
        libgdiplus \
        libx11-dev \
     && rm -rf /var/lib/apt/lists/*


WORKDIR /app

COPY ./out /app

ENTRYPOINT ["dotnet", "TelegramSearchBot.dll"]
