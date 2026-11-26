ARG ASPNET_RUNTIME_TAG=10.0.15
FROM mcr.microsoft.com/dotnet/aspnet:${ASPNET_RUNTIME_TAG} AS runtime

WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY publish/ .

ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000

ENTRYPOINT ["dotnet", "CP6.WebApi.dll"]
