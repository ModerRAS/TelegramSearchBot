FROM moderras/telegramsearchbot:build AS build-env
FROM moderras/telegramsearchbot:tessdata-latest AS tessdata

FROM mcr.microsoft.com/dotnet/runtime:5.0

RUN apt update -y && \
    apt install -y fontconfig
    apt install libleptonica-dev -y && \
    mkdir -p /app/x64 && \
    ln -s /usr/lib/x86_64-linux-gnu/liblept.so.5 /app/x64/liblept.so.5 && \
    ln -s /usr/lib/x86_64-linux-gnu/liblept.so.5 /app/x64/libleptonica-1.80.0.so && \
    apt install libtesseract-dev -y && \
    ln -s /usr/lib/x86_64-linux-gnu/libtesseract.so.4.0.1 /app/x64/libtesseract41.so

WORKDIR /app

COPY --from=tessdata /app/out/tessdata /app/tessdata
COPY --from=build-env /app/out /app

ENTRYPOINT ["dotnet", "TelegramSearchBot.dll"]