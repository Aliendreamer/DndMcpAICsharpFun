FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY DndMcpAICsharpFun.csproj .
COPY . .
RUN dotnet restore DndMcpAICsharpFun.csproj

RUN dotnet publish DndMcpAICsharpFun.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN mkdir -p /books /data

COPY --from=build /app/publish .

EXPOSE 5101

ENTRYPOINT ["dotnet", "DndMcpAICsharpFun.dll"]
