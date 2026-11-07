# One image for the whole app: the ASP.NET Core API, serving the built React frontend from wwwroot.

# 1. Build the frontend
FROM node:26-alpine AS frontend
WORKDIR /frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

# 2. Build the API (project files first, so the NuGet restore layer is cached between builds)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /src
COPY src/DocumentAssistant.Domain/*.csproj src/DocumentAssistant.Domain/
COPY src/DocumentAssistant.Application/*.csproj src/DocumentAssistant.Application/
COPY src/DocumentAssistant.Infrastructure/*.csproj src/DocumentAssistant.Infrastructure/
COPY src/DocumentAssistant.Api/*.csproj src/DocumentAssistant.Api/
RUN dotnet restore src/DocumentAssistant.Api
COPY src/ src/
RUN dotnet publish src/DocumentAssistant.Api -c Release --no-restore -o /app

# 3. Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=backend /app ./
COPY --from=frontend /frontend/dist ./wwwroot
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "DocumentAssistant.Api.dll"]
