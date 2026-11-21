# Subtitles

## Pick a subtitle

Open **Subtitles** in the player. Each of your languages has a tab, with:

- the subtitles in the file and next to it;
- downloads from OpenSubtitles.com;
- translations by Lingarr from a subtitle in another language.

A download or translation runs on the server. The player shows its progress and loads the subtitle when it is ready. **Timing** shifts a subtitle earlier or later in steps of 0.1 second.

## Requirements

- OpenSubtitles.com: an API key, and optionally your account. See [Configuration](../getting-started/configuration#subtitle-requests).
- Lingarr: its address, and path mappings when Lingarr sees the media under other paths.

Akira shows old subtitle files that are not UTF-8 in the code page of their language, for example Cyrillic for Russian. A file without a language in its name is read as Cyrillic or as Western European, by its letters. Put the language in the name, for example `Movie.pl.srt`, when the letters are wrong.

## Permissions

Akira saves a subtitle next to the video, for example `Movie.nl.srt`. It needs write access to the folder. The container runs as user `1654`, with the groups in `group_add`.

An error like `Could not write ... Access to the path ... is denied` means the folder is not writable for that group. Sonarr and Radarr make folders `755` by default, so new seasons often have this problem.

1. Find the folder's group: `ls -ldn "<folder>"`. The fourth column must be a group the container has in `group_add`.
2. Give the group write access to all existing folders:

    ```bash
    sudo find /path/to/movies /path/to/tv -type d ! -perm -g+w -exec chmod g+w {} +
    ```

3. In Sonarr and Radarr, go to **Settings > Media Management** and select **Show Advanced**. Under **Permissions**, select **Set Permissions**, set **chmod Folder** to `775` and **chown Group** to the media group.

With linuxserver.io images for Sonarr and Radarr, `UMASK=002` also makes new folders and files writable for the group.
