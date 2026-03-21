FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY SocialMediaBot/SocialMediaBot.ServiceDefaults/SocialMediaBot.ServiceDefaults.csproj SocialMediaBot.ServiceDefaults/
COPY SocialMediaBot/SocialMediaBot/SocialMediaBot.csproj SocialMediaBot/
RUN dotnet restore SocialMediaBot/SocialMediaBot.csproj

COPY SocialMediaBot/SocialMediaBot.ServiceDefaults/ SocialMediaBot.ServiceDefaults/
COPY SocialMediaBot/SocialMediaBot/ SocialMediaBot/
RUN dotnet publish SocialMediaBot/SocialMediaBot.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV DOTNET_EnableDiagnostics=false

ENTRYPOINT ["dotnet", "SocialMediaBot.dll"]
