FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ClockApp.sln ./
COPY ClockApp.Api/ClockApp.Api.csproj ClockApp.Api/
COPY ClockApp.Attendance/ClockApp.Attendance.csproj ClockApp.Attendance/
COPY ClockApp.Tests/ClockApp.Tests.csproj ClockApp.Tests/
RUN dotnet restore
COPY . .
RUN dotnet publish ClockApp.Api -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "ClockApp.Api.dll"]
