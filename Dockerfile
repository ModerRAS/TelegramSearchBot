FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build-env
WORKDIR /app

# Copy everything else and build
COPY . ./
RUN dotnet publish ./TelegramSearchBot/TelegramSearchBot.csproj -c Release -o out -r linux-x64 --self-contained false


FROM mcr.microsoft.com/dotnet/core/runtime:3.1
WORKDIR /app

COPY --from=build-env /app/out /app

ENTRYPOINT ["dotnet", "TelegramSearchBot.dll"]