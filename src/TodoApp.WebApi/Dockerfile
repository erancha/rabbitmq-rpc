FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src/TodoApp.WebApi
COPY ["src/TodoApp.WebApi/TodoApp.WebApi.csproj", "./"]
COPY ["src/TodoApp.Shared/TodoApp.Shared.csproj", "../TodoApp.Shared/"]
RUN dotnet restore
COPY src/TodoApp.WebApi/ ./
COPY src/TodoApp.Shared/ ../TodoApp.Shared/
RUN dotnet build "TodoApp.WebApi.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "TodoApp.WebApi.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TodoApp.WebApi.dll"]
