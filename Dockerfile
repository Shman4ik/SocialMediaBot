FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY SocialMediaBot.ServiceDefaults/SocialMediaBot.ServiceDefaults.csproj SocialMediaBot.ServiceDefaults/
COPY SocialMediaBot/SocialMediaBot.csproj SocialMediaBot/
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore SocialMediaBot/SocialMediaBot.csproj

COPY SocialMediaBot.ServiceDefaults/ SocialMediaBot.ServiceDefaults/
COPY SocialMediaBot/ SocialMediaBot/
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish SocialMediaBot/SocialMediaBot.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV DOTNET_EnableDiagnostics=false
EXPOSE 8080

ENTRYPOINT ["dotnet", "SocialMediaBot.dll"]
