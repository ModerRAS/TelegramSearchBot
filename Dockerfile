FROM moderras/telegramsearchbot:build AS build-env
FROM moderras/telegramsearchbot:tessdata-latest AS tessdata
FROM moderras/telegramsearchbot:tesseract-latest AS tesseract

FROM mcr.microsoft.com/dotnet/runtime:5.0

RUN apt update -y && \
    apt install -y fontconfig && \
    apt-get install -y --allow-unauthenticated \
        libc6-dev \
        libgdiplus \
        libx11-dev \
     && rm -rf /var/lib/apt/lists/*


WORKDIR /app

COPY --from=tessdata /app/out/tessdata /app/tessdata
COPY --from=tesseract /app/out/x64/tesseract41.so /app/x64/tesseract41.so
COPY --from=tesseract /app/out/x64/libleptonica-1.80.0.so /app/x64/libleptonica-1.80.0.so
COPY --from=build-env /app/out /app

ENTRYPOINT ["dotnet", "TelegramSearchBot.dll"]
