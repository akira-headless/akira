namespace Akira.Infrastructure.Transcoding;

public enum HardwareAcceleration
{
    None,
    // NVIDIA
    Nvenc,
    // Intel Quick Sync
    Qsv,
    // AMD and Intel on Linux, through /dev/dri
    Vaapi,
    // AMD on Windows
    Amf
}
