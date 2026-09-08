FROM mcr.microsoft.com/dotnet/runtime@sha256:0f8aaa92bdf4ea871aa0fc4c5903ca81fcae3af26d8fd2d6e918fdd226df3338
ARG CP6_SOURCE_SHA
LABEL org.opencontainers.image.source="https://github.com/GTX537/CP6" \
      org.opencontainers.image.revision="${CP6_SOURCE_SHA}" \
      org.opencontainers.image.version="0.10.1" \
      dev.cp6.p10.deployable="false"
WORKDIR /app
COPY --chown=0:0 --chmod=0555 publish/ /app/
COPY --chown=0:0 --chmod=0555 cosign /opt/cp6/cosign
ENV P10_COSIGN_PATH=/opt/cp6/cosign
USER 1654:1654
ENTRYPOINT ["/usr/bin/dotnet", "/app/CP6.P10.ReleaseVerifier.dll"]
CMD []
