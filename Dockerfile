# The watcher: React UI built with Node, served by ASP.NET Core. No Node needed on the host.
# Node from the ECR Public mirror of the Docker library, like Mosquitto in compose.yaml.
FROM public.ecr.aws/docker/library/node:22-alpine AS ui
WORKDIR /src
COPY web/package.json web/package-lock.json web/
RUN npm --prefix web ci
COPY web/ web/
RUN npm --prefix web run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/MqttChurn.Watch/ src/MqttChurn.Watch/
COPY --from=ui /src/src/MqttChurn.Watch/wwwroot/ src/MqttChurn.Watch/wwwroot/
RUN dotnet publish src/MqttChurn.Watch -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "MqttChurn.Watch.dll"]
