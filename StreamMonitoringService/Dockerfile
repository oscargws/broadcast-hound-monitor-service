# Use a .NET 8.0 SDK image
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build-env

# Set the working directory
WORKDIR /app

# Copy the csproj and restore dependencies
COPY *.csproj ./
RUN dotnet restore

# Copy the rest of the files and build the project
COPY . ./
RUN dotnet publish -c Release -o out

# Use a runtime image for the final stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0

# Set the working directory
WORKDIR /app

# Copy the build output from the build stage
COPY --from=build-env /app/out .

# Install FFmpeg
RUN apt-get update && \
    apt-get install -y ffmpeg

# Set the environment variable to indicate the location of FFmpeg
ENV PATH="/usr/bin/ffmpeg:/usr/bin/ffprobe:${PATH}"

# Expose port 80 for the application
EXPOSE 80

# Entry point for the application
ENTRYPOINT ["dotnet", "StreamMonitoringService.dll"]
