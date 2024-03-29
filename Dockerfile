FROM mcr.microsoft.com/dotnet/aspnet:7.0

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
