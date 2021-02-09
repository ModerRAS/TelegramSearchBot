FROM moderras/telegramsearchbot:build AS build-env
FROM moderras/telegramsearchbot:tessdata-latest AS tessdata

FROM mcr.microsoft.com/dotnet/runtime:5.0

RUN apt update -y && \
    apt install -y fontconfig

WORKDIR /app

COPY --from=build-env /app/out /app
COPY --from=tessdata /app/out/tessdata /app/tessdata

ENTRYPOINT ["dotnet", "TelegramSearchBot.dll"]