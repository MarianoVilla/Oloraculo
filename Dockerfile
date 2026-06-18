FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Oloraculo.sln ./
COPY Oloraculo.Web/Oloraculo.Web.csproj Oloraculo.Web/
COPY Oloraculo.Web.Tests/Oloraculo.Web.Tests.csproj Oloraculo.Web.Tests/
RUN dotnet restore Oloraculo.Web/Oloraculo.Web.csproj

COPY . .
RUN dotnet publish Oloraculo.Web/Oloraculo.Web.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_EnableDiagnostics=0

RUN mkdir -p /var/oloraculo
VOLUME ["/var/oloraculo"]
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Oloraculo.Web.dll"]
