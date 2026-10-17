FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build-env
WORKDIR DiscordBot

# Copy everything else and build
COPY . .
RUN dotnet restore
RUN dotnet publish -c Release -o out

# Build runtime image
FROM mcr.microsoft.com/dotnet/runtime:9.0
WORKDIR DiscordBot
COPY --from=build-env /DiscordBot/out .

# Set workdir to our permanent volume
WORKDIR /data

# Run the app on container startup
EXPOSE 8080/tcp
ENV DOTNET_GCHeapHardLimit=0x17D78400
ENV DOTNET_ENVIRONMENT=Production
ENTRYPOINT [ "dotnet", "/DiscordBot/DiscordBot.dll", "/DiscordBot/" ]
