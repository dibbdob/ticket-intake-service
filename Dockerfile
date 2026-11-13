# Build with the SDK; run on the chiseled ASP.NET image, which has no shell and no package manager, and
# runs as a non-root user by default.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Ticketdesk.Intake.slnx ./
COPY src/ src/
RUN dotnet publish src/Ticketdesk.Intake -c Release -o /app --nologo

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Ticketdesk.Intake.dll"]
