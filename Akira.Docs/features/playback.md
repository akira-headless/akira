# Playback

## How Akira picks what to play

When you press play, the app tells Akira what the device can play. Akira answers with a plan:

- **The version**: the best file the device can play, when Radarr has more than one. With a bitrate limit in the app, a smaller file that fits the limit comes before a large one that must be converted.
- **The tracks**: audio and subtitles by your track rules. A track you pick in the player is remembered for the movie, or for the whole show.
- **How**: the file as it is (**direct play**), or a converted stream (**HLS**) when the device cannot play the file.

A device that decodes HEVC, Dolby and DTS plays most files directly. A browser plays few formats, so it gets HLS more often.

## Watched and resume

Progress is saved every 10 seconds and when you stop. A video counts as watched after 90 percent. Progress under one minute does not resume. Watch state belongs to the movie or episode, so a new file from Radarr or Sonarr keeps it.

## Recap, intro, credits and Up next

Akira finds intros and credits from chapter names and from the audio of a season's episodes. It finds a recap ("Previously on") from chapter names, and from audio when the start of an episode is made of clips from the episodes before it. The player offers **Skip recap**, **Skip intro** and **Skip credits**, or skips them by itself when the settings say so. When the credits start, **Up next** offers the next episode.

## Hardware acceleration

Without it, Akira converts on the CPU. That is slow for 4K and HDR. The image has the drivers for Intel, AMD and NVIDIA. They do nothing until you give the container a GPU.

| GPU | Give the container | `Transcoding__HardwareAcceleration` | `Transcoding__ToneMapping` |
|-----|--------------------|-------------------------------------|----------------------------|
| Intel, such as an N100 or N150 | `/dev/dri` and the render group | `Vaapi` (or `Qsv`) | `Vaapi` (`OpenCl` with `Qsv`) |
| AMD | `/dev/dri` and the render group | `Vaapi` | `Vaapi` |
| NVIDIA | The GPU, through the NVIDIA Container Toolkit | `Nvenc` | `OpenCl` |

Tone mapping converts HDR to SDR for a device without HDR. Without a GPU setting, it runs on the CPU.

### Intel and AMD

1. On the host, find the render group: `getent group render | cut -d: -f3`.
2. Add `/dev/dri` and the render group to the container, and set the two settings:

    ```yaml
    services:
      akira:
        devices:
          - /dev/dri:/dev/dri
        group_add:
          - "1000" # the media group
          - "993"  # the render group
        environment:
          - Transcoding__HardwareAcceleration=Vaapi
          - Transcoding__ToneMapping=Vaapi
    ```

3. Recreate the container.
4. Check the GPU: `docker exec akira vainfo --display drm --device /dev/dri/renderD128`. It must list `VAProfileH264High` with `VAEntrypointEncSlice`.

### NVIDIA

1. Install the NVIDIA driver and the [NVIDIA Container Toolkit](https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/latest/install-guide.html) on the host.
2. Give the container the GPU, and set the two settings:

    ```yaml
    services:
      akira:
        deploy:
          resources:
            reservations:
              devices:
                - driver: nvidia
                  count: 1
                  capabilities: [gpu, video, compute, utility]
        environment:
          - Transcoding__HardwareAcceleration=Nvenc
          - Transcoding__ToneMapping=OpenCl
    ```

3. Recreate the container.
4. Check the GPU: `docker exec akira nvidia-smi`.

### Is the GPU used?

On the admin page, **Overview > Now playing** shows each playback from the moment the player starts to play it (an app that only opens the item is not shown). The encoder tells what converts:

| Encoder | Meaning |
|---------|---------|
| `h264_vaapi`, `h264_qsv`, `h264_nvenc` | The GPU converts. |
| `libx264` | The CPU converts. |
| `copy` | Only the sound is converted. |

The speed must be above 1x realtime. A conversion stops when it is 3 minutes ahead of the player and continues when the player gets near, so **Running** can show `false` while a video plays. When the GPU fails and the CPU works, Akira uses the CPU until the next restart and writes `Encoding with ... failed` to the log.
