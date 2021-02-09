FROM moderras/dotnetsdk:5.0 AS build-env
WORKDIR /app

# Copy everything else and build
COPY . ./
RUN dotnet publish ./TelegramSearchBot/TelegramSearchBot.csproj -c Release -o out -r linux-x64 --self-contained false && \
    apt update -y && \
    apt install wget zip -y && \
    wget https://github.com/tesseract-ocr/tessdata/archive/master.zip && \
    unzip master.zip && \
    mv tessdata-master/ out/tessdata


FROM mcr.microsoft.com/dotnet/runtime:5.0

RUN apt update -y && \
    apt install -y fontconfig

WORKDIR /app

COPY --from=build-env /app/out /app

ENTRYPOINT ["dotnet", "TelegramSearchBot.dll"]