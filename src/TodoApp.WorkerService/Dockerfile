FROM mcr.microsoft.com/dotnet/runtime:9.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /
COPY ["src/TodoApp.WorkerService/TodoApp.WorkerService.csproj", "src/TodoApp.WorkerService/TodoApp.WorkerService.csproj"]
COPY ["src/TodoApp.Shared/TodoApp.Shared.csproj", "src/TodoApp.Shared/TodoApp.Shared.csproj"]
RUN dotnet restore "src/TodoApp.WorkerService/TodoApp.WorkerService.csproj"
COPY src/ src/
WORKDIR "/src/TodoApp.WorkerService"
RUN dotnet build "TodoApp.WorkerService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "TodoApp.WorkerService.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TodoApp.WorkerService.dll"]
