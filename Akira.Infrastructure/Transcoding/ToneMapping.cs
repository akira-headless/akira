namespace Akira.Infrastructure.Transcoding;

// Where HDR is tone mapped to SDR. The GPU ways only apply while a GPU transcodes, and fall back to the CPU with it.
public enum ToneMapping
{
    // zscale and tonemap, every ffmpeg has them
    Cpu,
    // tonemap_opencl, with Nvenc, Qsv or Amf and the vendor's OpenCL driver
    OpenCl,
    // tonemap_vaapi, with Vaapi on Linux
    Vaapi
}
