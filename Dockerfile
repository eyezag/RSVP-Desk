# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props RsvpDesk.slnx ./
COPY src/RsvpDesk.Engine/RsvpDesk.Engine.csproj src/RsvpDesk.Engine/
COPY src/RsvpDesk.Web/RsvpDesk.Web.csproj src/RsvpDesk.Web/
RUN dotnet restore src/RsvpDesk.Web/RsvpDesk.Web.csproj
COPY src/ src/
RUN dotnet publish src/RsvpDesk.Web/RsvpDesk.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# SQLite file lives on a volume so the demo data survives restarts.
RUN mkdir -p /data && chown app:app /data
COPY --from=build /app .
USER app
ENV ASPNETCORE_URLS=http://+:8080 \
    ConnectionStrings__Rsvp="Data Source=/data/rsvpdesk.db"
EXPOSE 8080
VOLUME ["/data"]
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s \
  CMD ["dotnet", "RsvpDesk.Web.dll", "--healthcheck-probe"]
ENTRYPOINT ["dotnet", "RsvpDesk.Web.dll"]
