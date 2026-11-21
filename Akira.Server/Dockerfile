ARG BUILD_CONFIGURATION=Release
# The GPU drivers to add for transcoding on a GPU, comma separated, none by default:
#   intel   VAAPI and Quick Sync (the media driver with its encoders, oneVPL), OpenCL for tone mapping
#   amd     VAAPI (Mesa); tone map with Transcoding:ToneMapping=Vaapi
#   nvidia  OpenCL for tone mapping; NVENC itself comes from the NVIDIA container runtime
ARG GPU_DRIVERS=""

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
ARG GPU_DRIVERS
ARG TARGETARCH
WORKDIR /app
# ffprobe reads tracks and chapters, ffmpeg extracts subtitles and transcodes to HLS; Ubuntu's ffmpeg already has
# NVENC, Quick Sync, VAAPI and the OpenCL and VAAPI tone mapping, only the drivers below are missing
RUN apt-get update \
    && packages="ffmpeg" \
    && case ",$GPU_DRIVERS," in *,intel,*) if [ "${TARGETARCH:-amd64}" = "amd64" ]; then packages="$packages intel-media-va-driver-non-free libmfx-gen1.2 libvpl2 intel-opencl-icd vainfo"; fi ;; esac \
    && case ",$GPU_DRIVERS," in *,amd,*) packages="$packages mesa-va-drivers vainfo" ;; esac \
    && apt-get install -y --no-install-recommends $packages \
    && case ",$GPU_DRIVERS," in *,nvidia,*) mkdir -p /etc/OpenCL/vendors && echo "libnvidia-opencl.so.1" > /etc/OpenCL/vendors/nvidia.icd ;; esac \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/config /app/transcodes && chown $APP_UID /app/config /app/transcodes
# What the NVIDIA runtime mounts into the container: the encoder (video) and OpenCL (compute)
ENV NVIDIA_DRIVER_CAPABILITIES=compute,video,utility
EXPOSE 8080

# The web UI is plain files, built once on the build machine whatever the target platform
FROM --platform=$BUILDPLATFORM node:24.19-alpine AS web
WORKDIR /web
COPY ["Akira.Client/package.json", "Akira.Client/package-lock.json", "./"]
RUN npm ci --no-audit --no-fund
COPY Akira.Client/ ./
RUN npx vue-tsc --noEmit && npx vite build --outDir /web/dist --emptyOutDir

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION
ARG TARGETARCH
ARG VERSION=0.0.0-dev
WORKDIR /src
COPY ["Directory.Packages.props", "global.json", "./"]
COPY ["Akira.Server/Akira.Server.csproj", "Akira.Server/"]
COPY ["Akira.Contracts/Akira.Contracts.csproj", "Akira.Contracts/"]
COPY ["Akira.Application/Akira.Application.csproj", "Akira.Application/"]
COPY ["Akira.Core/Akira.Core.csproj", "Akira.Core/"]
COPY ["Akira.Infrastructure/Akira.Infrastructure.csproj", "Akira.Infrastructure/"]
COPY ["Akira.Migrations/Akira.Migrations.csproj", "Akira.Migrations/"]
RUN dotnet restore "Akira.Server/Akira.Server.csproj" -a $TARGETARCH
COPY Akira.Server/ Akira.Server/
COPY Akira.Contracts/ Akira.Contracts/
COPY Akira.Application/ Akira.Application/
COPY Akira.Core/ Akira.Core/
COPY Akira.Infrastructure/ Akira.Infrastructure/
COPY Akira.Migrations/ Akira.Migrations/
RUN dotnet publish "Akira.Server/Akira.Server.csproj" -c $BUILD_CONFIGURATION -a $TARGETARCH --no-restore \
    -o /app/publish /p:UseAppHost=false /p:Version=$VERSION

FROM base AS final
COPY --from=build /app/publish .
COPY --from=web /web/dist ./wwwroot
USER $APP_UID
VOLUME /app/config
# The aspnet image has no curl or wget, but it has bash, whose /dev/tcp makes the request without adding a package or
# a probe binary. The port follows ASPNETCORE_HTTP_PORTS, so the host network setup on 8484 is checked as well.
HEALTHCHECK --interval=30s --timeout=5s --start-period=2m --retries=3 \
    CMD ["bash", "-c", "port=${ASPNETCORE_HTTP_PORTS:-8080}; exec 3<>/dev/tcp/127.0.0.1/${port%%[,;]*} && printf 'GET /api/v1/health HTTP/1.0\\r\\nHost: localhost\\r\\n\\r\\n' >&3 && read -r status <&3 && [[ $status == *' 200 '* ]]"]
ENTRYPOINT ["dotnet", "Akira.Server.dll"]
