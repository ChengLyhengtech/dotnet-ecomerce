FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["Ecomerce-dotnet.csproj", "./"]
RUN dotnet restore "Ecomerce-dotnet.csproj"

COPY . .
RUN dotnet publish "Ecomerce-dotnet.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 80
ENV ASPNETCORE_HTTP_PORTS=80
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Ecomerce-dotnet.dll"]